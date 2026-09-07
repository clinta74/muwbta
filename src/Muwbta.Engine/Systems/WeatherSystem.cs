using Muwbta.Domain.Weather;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Time;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Systems;

/// <summary>
/// The sky, said out loud: what each world's weather is right now, and the line that goes out
/// when it changes (docs/WEATHER.md §4).
/// </summary>
/// <remarks>
/// <para>
/// <b>It computes nothing. <see cref="WeatherOracle"/> does that, and it is a pure function of
/// the clock</b>, so this holds no authority over the weather and cannot drift from it. What it
/// holds is the one thing arithmetic cannot supply: <em>what was said last time</em>. A
/// transition line only means something to somebody who was standing there before it, and knowing
/// who has already been told is not a property of the sky.
/// </para>
/// <para>
/// <b>Nothing here has a mechanical effect, by decision.</b> Weather touches every outdoor room in
/// every world at once, so any number hung on it is a world-wide balance change arriving on a
/// schedule nobody controls, and §4.4's multipliers are where difficulty is supposed to live.
/// Night is included in that: it is narrated and it does not darken a room. The `dark` flag stays
/// the only thing that decides whether a room can be seen, because making half of every outdoor
/// room unviewable for forty real minutes at a stretch would rewrite every zone ever authored
/// into a lantern-management problem.
/// </para>
/// <para>
/// <b>Indoors is where this flag finally does something.</b> It has been in the registry since
/// Phase 2 with no reader at all (BUGS.md #19). A sheltered room gets no standing line, no
/// transition line, and no notice that the day has turned - it is the difference between a room
/// with a sky over it and a room with a roof.
/// </para>
/// <para>
/// <b>Read the cache rather than the oracle when narrating.</b> Recomputing per player would give
/// two people in the same room lines from different microseconds; more to the point, the cached
/// value is what the previous line was measured against, so it is the only value a transition can
/// honestly be a transition from.
/// </para>
/// </remarks>
public sealed class WeatherSystem(IGameClock clock)
{
    private readonly IGameClock _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly Dictionary<string, WeatherState> _state = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeOfDay> _daylight = new(StringComparer.Ordinal);

    /// <summary>What time it is in the world, right now.</summary>
    public GameInstant Now => GameInstant.From(_clock.UtcNow);

    /// <summary>
    /// The sky over a world. Falls through to the oracle for a world nobody has ticked yet, so a
    /// <c>look</c> on the first pulse after start-up is right rather than empty.
    /// </summary>
    public WeatherState StateOf(string worldKey)
    {
        ArgumentNullException.ThrowIfNull(worldKey);

        return _state.TryGetValue(worldKey, out var known)
            ? known
            : WeatherOracle.At(worldKey, Now).State;
    }

    /// <summary>
    /// What a player standing in this room can see of the sky, or null when they cannot see it.
    /// </summary>
    public string? StandingLineFor(WorldState world, RoomKey room)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.IsFlagSet(room, RoomFlags.Indoors)
            ? null
            : WeatherNarration.Standing(StateOf(room.WorldKey));
    }

    /// <summary>
    /// Advances every world's sky and tells anyone outdoors what changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks players rather than rooms. A world can hold five hundred rooms and two people, and
    /// the audience for a weather line is the people - so this is O(players) and the room lookup
    /// is the flag check it was going to do anyway.
    /// </para>
    /// <para>
    /// <b>The first tick after a world loads narrates nothing.</b> There is no previous state to
    /// have changed from, and a player who logs in during a downpour should be told it is raining
    /// by their <c>look</c>, not told that it has just started.
    /// </para>
    /// </remarks>
    public void Tick(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var now = Now;

        foreach (var realm in world.AllWorlds)
        {
            var key = realm.Key;
            var reading = WeatherOracle.At(key, now);

            var hadWeather = _state.TryGetValue(key, out var previousWeather);
            var hadDaylight = _daylight.TryGetValue(key, out var previousDaylight);

            _state[key] = reading.State;
            _daylight[key] = now.TimeOfDay;

            if (!hadWeather || !hadDaylight)
            {
                continue;
            }

            var weatherLine = WeatherNarration.Transition(previousWeather, reading.State, now);

            var daylightLine = DaylightNarration.Transition(
                previousDaylight, now.TimeOfDay, reading.State, now);

            if (weatherLine is null && daylightLine is null)
            {
                continue;
            }

            Announce(world, key, daylightLine, weatherLine);
        }
    }

    /// <summary>
    /// Sends the lines to everyone outdoors in this world who is awake to notice.
    /// </summary>
    /// <remarks>
    /// The daylight line goes first when both land together, because that is the order they
    /// happen in: the light changing is what you notice before you notice what the sky is doing
    /// about it.
    /// </remarks>
    private static void Announce(
        WorldState world,
        string worldKey,
        string? daylightLine,
        string? weatherLine)
    {
        foreach (var actor in world.AllPlayers)
        {
            var room = actor.RoomKey;

            if (!string.Equals(room.WorldKey, worldKey, StringComparison.Ordinal))
            {
                continue;
            }

            // Asleep is the same answer as indoors here: both are people who are not watching the
            // sky, and both would be told about a sunset they had no way to see.
            if (actor.Character.RestState == Domain.Characters.CharacterRestState.Sleep)
            {
                continue;
            }

            if (world.IsFlagSet(room, RoomFlags.Indoors))
            {
                continue;
            }

            if (daylightLine is not null)
            {
                actor.SendText(daylightLine, "weather");
            }

            if (weatherLine is not null)
            {
                actor.SendText(weatherLine, "weather");
            }
        }
    }
}

namespace Muwbta.Domain.Weather;

/// <summary>
/// What the sky is doing, in words — the standing line a room description carries and the line
/// that announces a change (docs/WEATHER.md §4).
/// </summary>
/// <remarks>
/// <para>
/// <b>These lines ship with the engine and run in every world, so they name nothing that belongs
/// to one.</b> No gods, no myths, no places, no proper nouns of any kind, and no implication about
/// who or what is behind the weather — a line that says a storm was sent is a line that is wrong
/// in the next world somebody builds. Everything here is physical and observed: sky, cloud, light,
/// wind, air, ground. A world that wants its own voice for the sky can have authored overrides
/// later; it must not have to un-say something the engine already said.
/// </para>
/// <para>
/// <b>The transition line is the feature, not the standing line.</b> Weather with no mechanical
/// effect is worth having exactly as far as it is worth reading, and nobody reads a status field.
/// What a player notices is the moment it changes while they are standing in it, so every line
/// here describes a <em>change</em> — rain arriving, cloud breaking up, the wind dropping — rather
/// than restating the new state. Which is also why the tables are keyed on the direction of
/// travel: "the rain slackens to a drizzle" and "a fine rain starts up" are the same destination
/// and would be nonsense swapped.
/// </para>
/// <para>
/// The variant is chosen from the hour rather than at random, for the reason
/// <c>DreamSystem</c> gives: no random source has to be threaded anywhere, the same moment reads
/// the same way twice, and a week of weather still does not repeat one sentence.
/// </para>
/// </remarks>
public static class WeatherNarration
{
    private static readonly string[] ClearStanding =
    [
        "The sky is open and clear.",
    ];

    private static readonly string[] CloudyStanding =
    [
        "Loose cloud drifts across an otherwise open sky.",
    ];

    private static readonly string[] OvercastStanding =
    [
        "The sky is one unbroken sheet of grey.",
    ];

    private static readonly string[] FogStanding =
    [
        "Fog lies over everything, and the far side of things is guesswork.",
    ];

    private static readonly string[] DrizzleStanding =
    [
        "A fine rain is falling, more felt than seen.",
    ];

    private static readonly string[] RainStanding =
    [
        "Rain is falling steadily.",
    ];

    private static readonly string[] SnowStanding =
    [
        "Snow is falling, settling wherever it lands.",
    ];

    private static readonly string[] StormStanding =
    [
        "Wind and rain are coming in hard and sideways.",
    ];

    private static readonly string[] BlizzardStanding =
    [
        "Snow drives flat across the ground on a hard wind.",
    ];

    private static readonly string[] ClearEasing =
    [
        "The last of the cloud pulls apart, and the sky comes back.",
        "The weather lifts, and the light turns clean and long.",
        "The cover thins, tears, and lets the open sky through.",
    ];

    private static readonly string[] CloudyArriving =
    [
        "Cloud comes up over the horizon, and the light goes flat.",
        "The sky loses its edge as cloud gathers overhead.",
    ];

    private static readonly string[] CloudyEasing =
    [
        "The grey breaks into ragged cloud, with sky showing between the pieces.",
        "The cover thins to drifting cloud, and the day brightens a little.",
    ];

    private static readonly string[] OvercastArriving =
    [
        "The cloud thickens until the sky is one unbroken sheet.",
        "The last gaps close overhead, and everything goes the same shade of grey.",
    ];

    private static readonly string[] OvercastEasing =
    [
        "The weather eases off, leaving a low unbroken sky behind it.",
        "The worst of it passes, and the sky goes back to plain grey.",
    ];

    private static readonly string[] FogArriving =
    [
        "Mist gathers in the low ground, and the far side of things goes soft.",
        "The air thickens, and everything past a stone's throw turns to suggestion.",
    ];

    private static readonly string[] FogEasing =
    [
        "The fog thins, and the world past arm's reach comes back.",
        "The mist pulls apart into rags and then into nothing.",
    ];

    private static readonly string[] DrizzleArriving =
    [
        "A fine rain starts up, more felt than seen.",
        "The air turns wet without quite committing to rain.",
    ];

    private static readonly string[] DrizzleEasing =
    [
        "The rain slackens off to a drizzle.",
        "The downpour gives out, leaving a thin wet drift behind it.",
    ];

    private static readonly string[] RainArriving =
    [
        "The rain settles in, steady and unhurried.",
        "It begins to rain properly, straight down and without any fuss.",
    ];

    private static readonly string[] RainEasing =
    [
        "The wind drops away, and the storm settles into honest rain.",
        "The worst of it passes over, leaving steady rain behind.",
    ];

    private static readonly string[] SnowArriving =
    [
        "Snow begins to fall, drifting down without much conviction.",
        "The rain stiffens into snow, and the ground starts to keep it.",
    ];

    private static readonly string[] SnowEasing =
    [
        "The wind falls away, and the blizzard settles into ordinary snowfall.",
        "The driving stops, and the snow goes back to simply falling.",
    ];

    private static readonly string[] StormArriving =
    [
        "The wind gets up, and the rain starts coming in sideways.",
        "The sky opens; wind and rain arrive together and mean it.",
    ];

    /// <summary>
    /// Unreachable today, and kept as the fallback the <see cref="TransitionLines"/> remark
    /// describes: the only thing worse than a storm is its frozen twin, and that shares a rung
    /// with it, so easing into a storm goes through <see cref="TurningLines"/> instead.
    /// </summary>
    private static readonly string[] StormEasing =
    [
        "The weather turns, and the wind holds exactly where it was.",
    ];

    private static readonly string[] FreezingIntoSnow =
    [
        "The rain stiffens into snow, and the ground begins to keep it.",
        "It turns cold enough that what is falling comes down white.",
    ];

    private static readonly string[] ThawingIntoRain =
    [
        "The snow softens into rain, and what had settled starts to go.",
        "It warms just enough, and the snow turns to rain on the way down.",
    ];

    private static readonly string[] FreezingIntoBlizzard =
    [
        "The rain in the wind turns to snow, and the day goes white with it.",
        "The cold catches up with the wind, and the rain comes down as snow.",
    ];

    private static readonly string[] ThawingIntoStorm =
    [
        "The driven snow turns back to rain, though the wind keeps its temper.",
        "It warms without calming: the snow goes, the gale does not.",
    ];

    private static readonly string[] BlizzardArriving =
    [
        "The wind picks the snow up and drives it flat across the ground.",
        "The snow stops falling and starts travelling, and the distance goes with it.",
    ];

    /// <summary>What the sky is doing, for a room description or the <c>sky</c> verb.</summary>
    public static string Standing(WeatherState state) => Pick(StandingLines(state), 0);

    /// <summary>
    /// The line that announces a change, or null when there is nothing to announce.
    /// </summary>
    /// <remarks>
    /// Null on no change and null on the first reading of all, because a player who has just
    /// arrived has not watched anything happen. The standing line in their <c>look</c> is how they
    /// find out what the sky is doing; this is only ever for people who were already outside.
    /// </remarks>
    public static string? Transition(WeatherState? from, WeatherState to, GameInstant when)
    {
        if (from is not { } previous || previous == to)
        {
            return null;
        }

        // Same rung means the same weather at a different temperature - rain turning to snow, a
        // storm turning to a blizzard. Neither is an arrival and neither is a clearing, and
        // sending them down either table produces a line that is precisely backwards: "the
        // blizzard settles into ordinary snowfall" is a strange thing to read when it had been
        // raining a moment ago.
        var lines = Severity(to) == Severity(previous)
            ? TurningLines(to)
            : TransitionLines(to, Severity(to) > Severity(previous));

        return Pick(lines, when.TotalHours);
    }

    /// <summary>
    /// How much weather this is. Only the ordering matters, and only for deciding whether a
    /// change is an arrival or a clearing.
    /// </summary>
    /// <remarks>
    /// Rain and snow share a rung, and so do storm and blizzard: the pairs are the same weather at
    /// different temperatures, and a thaw that turns snow into rain has not made the day worse.
    /// </remarks>
    public static int Severity(WeatherState state) => state switch
    {
        WeatherState.Clear => 0,
        WeatherState.Cloudy => 1,
        WeatherState.Overcast => 2,
        WeatherState.Fog => 3,
        WeatherState.Drizzle => 4,
        WeatherState.Rain => 5,
        WeatherState.Snow => 5,
        WeatherState.Storm => 7,
        WeatherState.Blizzard => 7,
        _ => 0,
    };

    /// <summary>Whether this sky hides what is above it, which is what the daylight lines ask.</summary>
    public static bool HidesTheSky(WeatherState state) =>
        state is not (WeatherState.Clear or WeatherState.Cloudy);

    private static string[] StandingLines(WeatherState state) => state switch
    {
        WeatherState.Cloudy => CloudyStanding,
        WeatherState.Overcast => OvercastStanding,
        WeatherState.Fog => FogStanding,
        WeatherState.Drizzle => DrizzleStanding,
        WeatherState.Rain => RainStanding,
        WeatherState.Snow => SnowStanding,
        WeatherState.Storm => StormStanding,
        WeatherState.Blizzard => BlizzardStanding,
        _ => ClearStanding,
    };

    /// <summary>
    /// The lines for arriving at <paramref name="state"/> from better or worse weather.
    /// </summary>
    /// <remarks>
    /// Three of these have no easing table and one has no arriving table, because the transitions
    /// they would describe cannot happen: nothing is milder than clear to ease down from, and
    /// nothing is worse than a storm to ease down out of except its frozen twin. The fallback is
    /// the other direction's table rather than an exception — a classifier change that made one of
    /// those reachable should read a little oddly for an hour, not take the pulse down.
    /// </remarks>
    private static string[] TransitionLines(WeatherState state, bool worsening) => state switch
    {
        WeatherState.Clear => ClearEasing,
        WeatherState.Cloudy => worsening ? CloudyArriving : CloudyEasing,
        WeatherState.Overcast => worsening ? OvercastArriving : OvercastEasing,
        WeatherState.Fog => worsening ? FogArriving : FogEasing,
        WeatherState.Drizzle => worsening ? DrizzleArriving : DrizzleEasing,
        WeatherState.Rain => worsening ? RainArriving : RainEasing,
        WeatherState.Snow => worsening ? SnowArriving : SnowEasing,
        WeatherState.Storm => worsening ? StormArriving : StormEasing,
        WeatherState.Blizzard => BlizzardArriving,
        _ => ClearEasing,
    };

    /// <summary>
    /// The freeze and the thaw: the two pairs that share a rung, and the four ways across them.
    /// </summary>
    /// <remarks>
    /// Falls back to the standing line's table for anything else, which nothing can currently
    /// reach - two different states on the same rung is exactly the rain/snow and storm/blizzard
    /// pairs. A classifier change that added a third would read a little flatly for an hour rather
    /// than throwing inside the pulse.
    /// </remarks>
    private static string[] TurningLines(WeatherState to) => to switch
    {
        WeatherState.Snow => FreezingIntoSnow,
        WeatherState.Rain => ThawingIntoRain,
        WeatherState.Blizzard => FreezingIntoBlizzard,
        WeatherState.Storm => ThawingIntoStorm,
        _ => StandingLines(to),
    };

    private static string Pick(string[] lines, double hours)
    {
        var index = (int)(((long)Math.Floor(hours) % lines.Length + lines.Length) % lines.Length);
        return lines[index];
    }
}

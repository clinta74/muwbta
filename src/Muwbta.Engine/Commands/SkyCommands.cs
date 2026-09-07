using Muwbta.Domain.Weather;
using Muwbta.Domain.Worlds;

namespace Muwbta.Engine.Commands;

/// <summary>
/// <c>sky</c> — what time of year it is, what time of day it is, and what the weather is doing.
/// </summary>
/// <remarks>
/// <para>
/// <b>One verb for the date and the weather, because they are one question.</b> There was no
/// <c>time</c> verb before this and there should not be two now: "what season is it" and "is it
/// still raining" are asked in the same breath by somebody deciding whether to walk somewhere.
/// </para>
/// <para>
/// <b>Named <c>sky</c> rather than <c>weather</c> for the namespace.</b> Root verbs are one shared
/// prefix space (§13) and this is a thing typed idly, so it should be cheap: <c>sk</c> was
/// unclaimed, while <c>weather</c> would have needed four characters to clear <c>wear</c>.
/// </para>
/// <para>
/// <b>Indoors it still tells the time.</b> Losing sight of the sky is not losing track of the day,
/// and a player who has been in a cellar for ten minutes has a fair idea what the hour is. What
/// they do not get is the weather, and they are told that is why.
/// </para>
/// </remarks>
public static class SkyCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "sky", 2, "sky - the season, the hour, and what the weather is doing", Sky));
    }

    /// <summary>What the player is told, given everything the handler had to look up.</summary>
    /// <remarks>
    /// Split out from the handler so the wording can be tested without a world, a clock and a
    /// player standing in a room. The two callers of that arrangement - the test and the game -
    /// then cannot disagree about it.
    /// </remarks>
    public static string Describe(GameInstant when, WeatherState? weather)
    {
        var date =
            $"It is {DaylightNarration.Describe(when.TimeOfDay)}, day {when.DayOfSeason} of "
            + $"{when.Season}, in the year {when.Year}.";

        return weather is { } sky
            ? $"{date} {WeatherNarration.Standing(sky)}"
            : $"{date} You cannot see the sky from in here.";
    }

    private static void Sky(CommandContext ctx)
    {
        var room = ctx.Actor.Character.RoomKey;

        if (ctx.Weather is not { } weather)
        {
            // The engine can run without the system - a test host, mostly - and a verb that threw
            // there would be a verb that took the pulse down over a missing optional dependency.
            ctx.Reply("You have no sense of the time or the weather here.");
            return;
        }

        var indoors = ctx.World.IsFlagSet(room, RoomFlags.Indoors);

        ctx.Reply(Describe(
            weather.Now,
            indoors ? null : weather.StateOf(room.WorldKey)));
    }
}

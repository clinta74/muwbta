namespace Muwbta.Domain.Weather;

/// <summary>
/// The turns of the day, in words — first light, the sun going down, the dark coming on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same rule as <see cref="WeatherNarration"/>: nothing here belongs to any one world.</b> No
/// gods, no named bodies in the sky beyond the plain sun and stars anyone standing outside can
/// see, and no suggestion that the day turning is anybody's doing. A world with its own account of
/// where the light goes at night should be free to tell it without contradicting a line the engine
/// already printed.
/// </para>
/// <para>
/// <b>Weather-aware, because otherwise it lies.</b> Announcing that the sun clears the horizon
/// under an unbroken sheet of grey is the sort of detail that tells a player the world is a table
/// of strings. Every turn of the day has an open-sky line and a covered-sky one, chosen from what
/// the weather is actually doing.
/// </para>
/// <para>
/// Night gets no arrival line of its own from dusk when the sky is covered, and that is
/// deliberate: <em>nothing</em> visible happens at that moment beyond the grey going dark, so the
/// line says exactly that and no more.
/// </para>
/// </remarks>
public static class DaylightNarration
{
    private static readonly string[] DawnOpen =
    [
        "The sky pales along the horizon.",
        "First light comes up, thin and grey, and the stars go out one by one.",
    ];

    private static readonly string[] DawnCovered =
    [
        "The dark overhead lightens by degrees, without ever showing a horizon.",
        "Somewhere behind the cloud it is getting light.",
    ];

    private static readonly string[] MorningOpen =
    [
        "The sun clears the horizon, and the day starts in earnest.",
        "The light comes up properly, and the long shadows shorten.",
    ];

    private static readonly string[] MorningCovered =
    [
        "The light comes up as far as it is going to, and the day starts.",
        "The grey overhead brightens to the colour of a working morning.",
    ];

    private static readonly string[] AfternoonOpen =
    [
        "The sun passes its height and begins the long way down.",
        "The shadows turn and start to lengthen the other way.",
    ];

    private static readonly string[] AfternoonCovered =
    [
        "The day tips over into afternoon with nothing overhead to mark it.",
        "The flat light overhead loses its last bit of warmth.",
    ];

    private static readonly string[] DuskOpen =
    [
        "The light goes long and red, and the day begins to end.",
        "The sun settles toward the horizon and takes the warmth with it.",
    ];

    private static readonly string[] DuskCovered =
    [
        "The grey overhead darkens toward evening.",
        "The light drains out of the day without any ceremony.",
    ];

    private static readonly string[] NightOpen =
    [
        "The last of the light goes, and the stars come out.",
        "Full dark, and the sky fills with stars.",
    ];

    private static readonly string[] NightCovered =
    [
        "The last of the light goes. There is nothing overhead but dark.",
        "Night closes in under cloud, and it is very dark indeed.",
    ];

    /// <summary>
    /// The line for the day turning, or null when it has not.
    /// </summary>
    public static string? Transition(
        TimeOfDay? from,
        TimeOfDay to,
        WeatherState weather,
        GameInstant when)
    {
        if (from is not { } previous || previous == to)
        {
            return null;
        }

        var covered = WeatherNarration.HidesTheSky(weather);

        var lines = to switch
        {
            TimeOfDay.Dawn => covered ? DawnCovered : DawnOpen,
            TimeOfDay.Morning => covered ? MorningCovered : MorningOpen,
            TimeOfDay.Afternoon => covered ? AfternoonCovered : AfternoonOpen,
            TimeOfDay.Dusk => covered ? DuskCovered : DuskOpen,
            _ => covered ? NightCovered : NightOpen,
        };

        var index = (int)(((long)Math.Floor(when.TotalHours) % lines.Length + lines.Length)
            % lines.Length);

        return lines[index];
    }

    /// <summary>How a player would say what time it is, for the <c>sky</c> verb.</summary>
    public static string Describe(TimeOfDay timeOfDay) => timeOfDay switch
    {
        TimeOfDay.Dawn => "first light",
        TimeOfDay.Morning => "morning",
        TimeOfDay.Afternoon => "afternoon",
        TimeOfDay.Dusk => "dusk",
        _ => "night",
    };
}

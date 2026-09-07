namespace Muwbta.Domain.Weather;

/// <summary>
/// The numbers that make one realm's sky different from another's (docs/WEATHER.md §1).
/// </summary>
/// <param name="MeanTemperature">Degrees at the spring equinox, mid-afternoon.</param>
/// <param name="SeasonalSwing">Degrees either side of the mean between midsummer and midwinter.</param>
/// <param name="DiurnalSwing">Degrees either side between the coldest hour and the warmest.</param>
/// <param name="WeatherSwing">Degrees a front can move the temperature either way.</param>
/// <param name="BaseMoisture">Where this climate sits on the dry-to-wet scale, 0 to 1.</param>
/// <param name="SeasonalMoistureSwing">How much wetter its wet half is than its dry half.</param>
/// <param name="Windiness">Where it sits on the still-to-gale scale, 0 to 1.</param>
public readonly record struct ClimateProfile(
    double MeanTemperature,
    double SeasonalSwing,
    double DiurnalSwing,
    double WeatherSwing,
    double BaseMoisture,
    double SeasonalMoistureSwing,
    double Windiness);

/// <summary>
/// The climates a world can have, and the one every world has today.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing authored selects one yet, and that is a known gap rather than an oversight.</b>
/// Every world resolves <see cref="Temperate"/> through <see cref="For"/>. The design
/// (docs/WEATHER.md §1) wants <c>climate</c> to be an inherited text flag so a world declares
/// <c>arid</c> once and an alpine zone inside it overrides — which needs the flag registry to
/// grow a text-valued kind, and needs both builder flag panels, the <c>rflag</c> verb, the flag
/// DTO and the bundle validator to stop assuming booleans. That is a sensible change and a
/// disproportionate one to make on the way to a system with no mechanical effect.
/// </para>
/// <para>
/// The profiles are written and tested regardless, because they are the design rather than
/// speculation: when a source for the value lands, <see cref="For"/> is the single call site that
/// changes and everything downstream already takes a profile.
/// </para>
/// </remarks>
public static class Climates
{
    /// <summary>Four real seasons, rain spread through the year and heavier in the cold half.</summary>
    public static readonly ClimateProfile Temperate = new(
        MeanTemperature: 11,
        SeasonalSwing: 12,
        DiurnalSwing: 5,
        WeatherSwing: 4,
        BaseMoisture: 0.44,
        SeasonalMoistureSwing: 0.10,
        Windiness: 0.34);

    /// <summary>Milder either way and wetter throughout, with the wind off the water.</summary>
    public static readonly ClimateProfile Coastal = new(
        MeanTemperature: 12,
        SeasonalSwing: 8,
        DiurnalSwing: 3,
        WeatherSwing: 4,
        BaseMoisture: 0.52,
        SeasonalMoistureSwing: 0.08,
        Windiness: 0.48);

    /// <summary>Hot, dry, and swinging hard between noon and dawn.</summary>
    public static readonly ClimateProfile Arid = new(
        MeanTemperature: 24,
        SeasonalSwing: 10,
        DiurnalSwing: 12,
        WeatherSwing: 3,
        BaseMoisture: 0.18,
        SeasonalMoistureSwing: 0.06,
        Windiness: 0.30);

    /// <summary>Cold enough that most of the year's weather arrives as snow.</summary>
    public static readonly ClimateProfile Alpine = new(
        MeanTemperature: 1,
        SeasonalSwing: 13,
        DiurnalSwing: 7,
        WeatherSwing: 5,
        BaseMoisture: 0.48,
        SeasonalMoistureSwing: 0.06,
        Windiness: 0.52);

    /// <summary>
    /// No sky at all: one temperature all year and nothing falling out of anything.
    /// </summary>
    /// <remarks>
    /// This is how an underground realm stops having weather without every room in it carrying
    /// <c>indoors</c> — the classifier returns <see cref="WeatherState.Clear"/> at this moisture
    /// forever, and a cave that says "the air is still and close" is saying the true thing.
    /// </remarks>
    public static readonly ClimateProfile Subterranean = new(
        MeanTemperature: 9,
        SeasonalSwing: 0.5,
        DiurnalSwing: 0.5,
        WeatherSwing: 0.5,
        BaseMoisture: 0.30,
        SeasonalMoistureSwing: 0,
        Windiness: 0.05);

    /// <summary>
    /// The climate a world runs on. Temperate for everything, until content can say otherwise.
    /// </summary>
    public static ClimateProfile For(string worldKey)
    {
        ArgumentNullException.ThrowIfNull(worldKey);
        return Temperate;
    }
}

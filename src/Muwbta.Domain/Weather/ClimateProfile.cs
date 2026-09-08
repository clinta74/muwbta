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
/// <param name="MoistureVariability">
/// How far a front can move the moisture either way. Zero is a climate with no fronts at all -
/// the moisture is exactly <paramref name="BaseMoisture"/> forever - which is what makes an
/// underground realm weatherless rather than merely dry.
/// </param>
/// <param name="WindVariability">The same, for the wind.</param>
public readonly record struct ClimateProfile(
    double MeanTemperature,
    double SeasonalSwing,
    double DiurnalSwing,
    double WeatherSwing,
    double BaseMoisture,
    double SeasonalMoistureSwing,
    double Windiness,
    double MoistureVariability,
    double WindVariability);

/// <summary>
/// The climates a world can have, keyed by the <c>climate</c> flag's choices.
/// </summary>
/// <remarks>
/// <para>
/// The names here are exactly <see cref="Worlds.RoomFlags.Climate"/>'s choices, and
/// <see cref="For"/> is the only thing that maps between them. That is checked by a test rather
/// than by the type system, because the registry lives in <c>Worlds</c> and the numbers live here,
/// and a reference from the flag registry into the weather model would be the wrong direction: the
/// registry should not know what a climate is <em>for</em>.
/// </para>
/// <para>
/// An unrecognised name resolves to <see cref="Temperate"/>. That cannot happen through the API,
/// which refuses a choice the registry does not know, and the resolution chain drops one anyway -
/// this is the third guard, for a value that reached the database some other way.
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
        Windiness: 0.34,
        MoistureVariability: 0.55,
        WindVariability: 0.6);

    /// <summary>Milder either way and wetter throughout, with the wind off the water.</summary>
    public static readonly ClimateProfile Coastal = new(
        MeanTemperature: 12,
        SeasonalSwing: 8,
        DiurnalSwing: 3,
        WeatherSwing: 4,
        BaseMoisture: 0.52,
        SeasonalMoistureSwing: 0.08,
        Windiness: 0.48,
        MoistureVariability: 0.55,
        WindVariability: 0.6);

    /// <summary>Hot, dry, and swinging hard between noon and dawn.</summary>
    public static readonly ClimateProfile Arid = new(
        MeanTemperature: 24,
        SeasonalSwing: 10,
        DiurnalSwing: 12,
        WeatherSwing: 3,
        BaseMoisture: 0.18,
        SeasonalMoistureSwing: 0.06,
        Windiness: 0.30,
        MoistureVariability: 0.55,
        WindVariability: 0.6);

    /// <summary>Cold enough that most of the year's weather arrives as snow.</summary>
    public static readonly ClimateProfile Alpine = new(
        MeanTemperature: 1,
        SeasonalSwing: 13,
        DiurnalSwing: 7,
        WeatherSwing: 5,
        BaseMoisture: 0.48,
        SeasonalMoistureSwing: 0.06,
        Windiness: 0.52,
        MoistureVariability: 0.55,
        WindVariability: 0.6);

    /// <summary>
    /// No sky at all: one temperature all year and nothing falling out of anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How an underground realm stops having weather without every room in it carrying
    /// <c>indoors</c>. The moisture sits below the first threshold and never moves, so the
    /// classifier answers <see cref="WeatherState.Clear"/> forever, and a cave that says "the air
    /// is still and close" is saying the true thing.
    /// </para>
    /// <para>
    /// <b>The zeroes are the whole profile.</b> This said the same thing in prose while the noise
    /// amplitudes were fixed in the oracle rather than declared here, so it rained underground and
    /// the comment claiming otherwise was simply wrong - caught by the test that asserts the hard
    /// zero rather than "rarely".
    /// </para>
    /// </remarks>
    public static readonly ClimateProfile Subterranean = new(
        MeanTemperature: 9,
        SeasonalSwing: 0,
        DiurnalSwing: 0,
        WeatherSwing: 0,
        BaseMoisture: 0.30,
        SeasonalMoistureSwing: 0,
        Windiness: 0.05,
        MoistureVariability: 0,
        WindVariability: 0);

    /// <summary>
    /// Wrong weather: warm when it should not be, and the wet half of the year gone dry.
    /// </summary>
    /// <remarks>
    /// For a place where the seasons have stopped keeping the office. The seasonal swing runs
    /// <em>backwards</em> - it is warmest at midwinter - which is not a value a real climate takes
    /// and is the point: somewhere that has come loose should read as loose to anybody paying
    /// attention over a game year, without a single line of prose saying so.
    /// </remarks>
    public static readonly ClimateProfile Blighted = new(
        MeanTemperature: 14,
        SeasonalSwing: -7,
        DiurnalSwing: 3,
        WeatherSwing: 6,
        BaseMoisture: 0.34,
        SeasonalMoistureSwing: -0.08,
        Windiness: 0.40,
        MoistureVariability: 0.55,
        WindVariability: 0.6);

    /// <summary>The profile for a climate name, defaulting to temperate for anything unknown.</summary>
    public static ClimateProfile For(string? climate) => climate switch
    {
        "coastal" => Coastal,
        "arid" => Arid,
        "alpine" => Alpine,
        "subterranean" => Subterranean,
        "blighted" => Blighted,
        _ => Temperate,
    };
}

namespace Muwbta.Domain.Weather;

public enum WeatherState
{
    Clear = 0,
    Cloudy = 1,
    Overcast = 2,
    Fog = 3,
    Drizzle = 4,
    Rain = 5,
    Snow = 6,
    Storm = 7,
    Blizzard = 8,
}

/// <param name="Temperature">Degrees, for anything that wants to know about freezing.</param>
/// <param name="Moisture">0 (bone dry) to 1 (as wet as this sky gets).</param>
/// <param name="Wind">0 (still) to 1 (gale).</param>
public readonly record struct WeatherReading(
    WeatherState State,
    double Temperature,
    double Moisture,
    double Wind);

/// <summary>
/// What the sky over a world is doing, as a pure function of the world, its climate and the time
/// (docs/WEATHER.md §3, option B).
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived rather than walked, so there is nothing to persist and nothing to lose.</b> A
/// stored Markov chain is the conventional answer and it was the other candidate; this is the
/// same answer with three properties the chain cannot have. It survives a restart mid-blizzard
/// without a row, because the sky is a function of the clock and the clock does not restart. It
/// is replayable, so a test can assert on midwinter in Ossara rather than sampling. And it can be
/// asked about the future, which is worth more than it sounds — a builder writing a quest that
/// wants fog can find out when the fog is.
/// </para>
/// <para>
/// <b>Smoothness is the reason it does not feel random.</b> The three scalars are value noise
/// interpolated between cells two game days, fourteen hours and five hours apart, so the weather
/// wanders rather than jumping: it takes a couple of game hours to go from overcast to rain, and
/// it cannot flicker between clear and storm on consecutive readings. The transition narration
/// (<see cref="WeatherNarration"/>) leans on that — every line it can print describes a change
/// that actually happened gradually.
/// </para>
/// <para>
/// <b>Every world gets its own sky.</b> The noise is seeded from the world key, so the Reaches'
/// five realms have genuinely independent weather rather than one storm crossing all of them at
/// once. Seasons stay in step, because they come from the shared calendar.
/// </para>
/// </remarks>
public static class WeatherOracle
{
    public static WeatherReading At(string worldKey, ClimateProfile climate, GameInstant when)
    {
        ArgumentNullException.ThrowIfNull(worldKey);

        var seed = SeedFor(worldKey);
        var hours = when.TotalHours;

        // Three independent walks. The offsets keep them from being the same wander three times.
        var frontNoise = WeatherNoise.Layered(seed, hours);
        var moistureNoise = WeatherNoise.Layered(seed ^ 0x5bf03635, hours);
        var windNoise = WeatherNoise.Layered(seed ^ 0x27d4eb2f, hours);

        // Peaks at midsummer, troughs at midwinter. YearPhase 0 is the first day of Spring, so
        // the quarter-turn offset is what puts the peak a season and a half in.
        var seasonal = Math.Sin(2 * Math.PI * (when.YearPhase - 0.125));

        // Warmest mid-afternoon, coldest an hour or so before dawn.
        var diurnal = Math.Sin(2 * Math.PI * (when.DayPhase - 0.375));

        var temperature =
            climate.MeanTemperature
            + (climate.SeasonalSwing * seasonal)
            + (climate.DiurnalSwing * diurnal)
            + (climate.WeatherSwing * Centred(frontNoise));

        var moisture = Math.Clamp(
            climate.BaseMoisture
            - (climate.SeasonalMoistureSwing * seasonal)
            + (0.55 * Centred(moistureNoise)),
            0,
            1);

        var wind = Math.Clamp(climate.Windiness + (0.6 * Centred(windNoise)), 0, 1);

        return new WeatherReading(
            Classify(temperature, moisture, wind, when.TimeOfDay),
            temperature,
            moisture,
            wind);
    }

    /// <summary>The world's sky right now, at its own climate.</summary>
    public static WeatherReading At(string worldKey, GameInstant when) =>
        At(worldKey, Climates.For(worldKey), when);

    /// <summary>
    /// A stable hash of the world key. Deliberately not <c>string.GetHashCode</c>, which is
    /// randomised per process — the whole point of this file is that two runs agree.
    /// </summary>
    public static int SeedFor(string worldKey)
    {
        ArgumentNullException.ThrowIfNull(worldKey);

        unchecked
        {
            var hash = 2166136261u;

            foreach (var c in worldKey)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int)hash;
        }
    }

    /// <summary>
    /// The triple to a named sky.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered wettest-first, so each clause is "and not any of the wetter ones". The thresholds
    /// are cut against a temperate year and checked by <c>WeatherDistributionTests</c>: fair
    /// weather most of the time, rain a regular event, and a storm something you remember. Moving
    /// one of them moves the whole distribution, which is why that test asserts on shares rather
    /// than on any single reading.
    /// </para>
    /// <para>
    /// <b>Fog is the one that reads the clock</b>, because fog that burns off at ten in the
    /// morning is fog and fog that sits there all afternoon is a bug. It needs damp, still air and
    /// no sun, which is also why it never appears in a gale.
    /// </para>
    /// </remarks>
    private static WeatherState Classify(
        double temperature,
        double moisture,
        double wind,
        TimeOfDay timeOfDay)
    {
        var freezing = temperature < 0;

        if (moisture >= 0.80 && wind >= 0.58)
        {
            return freezing ? WeatherState.Blizzard : WeatherState.Storm;
        }

        if (moisture >= 0.74)
        {
            return freezing ? WeatherState.Snow : WeatherState.Rain;
        }

        if (moisture >= 0.64)
        {
            return freezing ? WeatherState.Snow : WeatherState.Drizzle;
        }

        if (moisture >= 0.52)
        {
            return WeatherState.Overcast;
        }

        if (moisture >= 0.42
            && wind <= 0.22
            && !freezing
            && timeOfDay is TimeOfDay.Night or TimeOfDay.Dawn)
        {
            return WeatherState.Fog;
        }

        return moisture >= 0.38 ? WeatherState.Cloudy : WeatherState.Clear;
    }

    /// <summary>Noise in 0..1 to a signed -1..1 nudge.</summary>
    private static double Centred(double noise) => (noise - 0.5) * 2;
}

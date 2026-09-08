using Muwbta.Domain.Weather;

namespace Muwbta.Domain.Tests.Weather;

/// <summary>
/// What a year of sky actually looks like, sampled rather than reasoned about.
/// </summary>
/// <remarks>
/// <para>
/// The classifier's thresholds are the one part of derived weather that cannot be checked by
/// reading it: they are cuts through three continuous distributions, and moving any one of them
/// moves everything on the wet side of it. So this walks a full game year an hour at a time and
/// asserts on shares.
/// </para>
/// <para>
/// <b>Asserted as wide bands, on purpose.</b> The claim worth defending is "fair weather is
/// normal, rain is a regular event, a storm is worth remembering" — not any particular
/// percentage. A test that pinned the numbers would fail on every tuning pass and teach whoever
/// was tuning to update the expectations without reading them.
/// </para>
/// </remarks>
public sealed class WeatherDistributionTests
{
    private const string World = "ossara";

    /// <summary>Stated rather than defaulted, because every band below is cut against it.</summary>
    private const string Climate = "temperate";

    private static Dictionary<WeatherState, int> WalkAYear(string worldKey)
    {
        var counts = new Dictionary<WeatherState, int>();

        for (var hour = 0; hour < GameInstant.HoursPerYear; hour++)
        {
            var reading = WeatherOracle.At(worldKey, Climate, GameInstant.FromGameHours(hour));
            counts[reading.State] = counts.GetValueOrDefault(reading.State) + 1;
        }

        return counts;
    }

    private static double Share(Dictionary<WeatherState, int> counts, params WeatherState[] states)
        => states.Sum(s => counts.GetValueOrDefault(s)) / (double)GameInstant.HoursPerYear;

    [Fact]
    public void A_year_is_mostly_fair()
    {
        var counts = WalkAYear(World);
        var fair = Share(counts, WeatherState.Clear, WeatherState.Cloudy);

        Assert.InRange(fair, 0.30, 0.75);
    }

    [Fact]
    public void It_rains_often_enough_to_be_a_normal_thing()
    {
        var counts = WalkAYear(World);
        var wet = Share(
            counts,
            WeatherState.Drizzle,
            WeatherState.Rain,
            WeatherState.Snow,
            WeatherState.Storm,
            WeatherState.Blizzard);

        Assert.InRange(wet, 0.12, 0.45);
    }

    [Fact]
    public void Storms_are_rare_enough_to_be_worth_remembering()
    {
        var counts = WalkAYear(World);
        var violent = Share(counts, WeatherState.Storm, WeatherState.Blizzard);

        Assert.InRange(violent, 0.001, 0.06);
    }

    [Fact]
    public void Every_state_the_classifier_can_reach_is_reached_within_a_year()
    {
        // Snow and blizzard need a cold enough hour, which a temperate winter supplies; fog needs
        // a still, damp night. If one of these stops appearing, a threshold has been moved past
        // the point where a whole kind of weather still exists.
        var counts = WalkAYear(World);

        foreach (var state in Enum.GetValues<WeatherState>())
        {
            Assert.True(counts.GetValueOrDefault(state) > 0, $"{state} never happened in a year");
        }
    }

    [Fact]
    public void Winter_is_colder_than_summer()
    {
        var midsummer = WeatherOracle.At(
            World,
            Climate,
            GameInstant.FromGameHours(GameInstant.HoursPerYear * 0.375));

        var midwinter = WeatherOracle.At(
            World,
            Climate,
            GameInstant.FromGameHours(GameInstant.HoursPerYear * 0.875));

        Assert.True(
            midwinter.Temperature < midsummer.Temperature,
            $"midwinter {midwinter.Temperature:F1} was not colder than midsummer {midsummer.Temperature:F1}");
    }

    [Fact]
    public void Snow_only_falls_in_the_cold_half_of_the_year()
    {
        for (var hour = 0; hour < GameInstant.HoursPerYear; hour++)
        {
            var when = GameInstant.FromGameHours(hour);
            var reading = WeatherOracle.At(World, Climate, when);

            if (reading.State is WeatherState.Snow or WeatherState.Blizzard)
            {
                Assert.True(
                    reading.Temperature < 0,
                    $"snow at {reading.Temperature:F1} degrees on day {when.DayOfYear}");
            }
        }
    }

    [Fact]
    public void Two_worlds_do_not_share_a_sky()
    {
        // The point of seeding per world: a storm over one realm is not a storm over all five.
        var differences = 0;

        for (var hour = 0; hour < GameInstant.HoursPerYear; hour += 3)
        {
            var when = GameInstant.FromGameHours(hour);

            if (WeatherOracle.At("ossara", Climate, when).State != WeatherOracle.At("khaldra", Climate, when).State)
            {
                differences++;
            }
        }

        Assert.True(differences > 100, $"only {differences} sampled hours differed between worlds");
    }

    [Fact]
    public void The_same_hour_reads_the_same_way_every_time()
    {
        // The whole reason nothing is persisted. A restart lands on this same arithmetic.
        var when = GameInstant.FromGameHours(4_211.5);

        Assert.Equal(
            WeatherOracle.At(World, Climate, when),
            WeatherOracle.At(World, Climate, when));
    }

    [Fact]
    public void Weather_wanders_rather_than_jumping()
    {
        // Smoothness is what the transition narration depends on: every line it can print
        // describes a change that happened by degrees, so "the rain settles in" must never be the
        // announcement that an open sky has become a downpour between one reading and the next.
        //
        // Three rungs in a game hour - five real minutes - is allowed, and is what rain stopping
        // looks like. What is asserted is that fair weather is never one step from a soaking.
        var worst = 0;
        var worstAt = 0;

        for (var hour = 0; hour < GameInstant.HoursPerYear; hour++)
        {
            var before = WeatherOracle.At(World, Climate, GameInstant.FromGameHours(hour)).State;
            var after = WeatherOracle.At(World, Climate, GameInstant.FromGameHours(hour + 1)).State;

            // Fog is the deliberate exception, and the only state that turns on the clock rather
            // than on the three scalars: it can be there at dawn and gone by sunrise with nothing
            // else having moved, which is what fog does.
            if (before is WeatherState.Fog || after is WeatherState.Fog)
            {
                continue;
            }

            var low = Math.Min(WeatherNarration.Severity(before), WeatherNarration.Severity(after));
            var high = Math.Max(WeatherNarration.Severity(before), WeatherNarration.Severity(after));

            Assert.False(
                low <= WeatherNarration.Severity(WeatherState.Cloudy)
                    && high >= WeatherNarration.Severity(WeatherState.Rain),
                $"{before} became {after} in one game hour, at hour {hour}");

            if (high - low > worst)
            {
                worst = high - low;
                worstAt = hour;
            }
        }

        Assert.True(worst <= 3, $"the weather jumped {worst} rungs at hour {worstAt}");
    }
}

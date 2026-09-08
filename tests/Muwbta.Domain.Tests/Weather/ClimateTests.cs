using Muwbta.Domain.Weather;

namespace Muwbta.Domain.Tests.Weather;

/// <summary>
/// That the climate actually changes the sky, sampled over a game year.
/// </summary>
/// <remarks>
/// <b>Asserted as comparisons between climates rather than as numbers.</b> "Arid is drier than
/// temperate" survives a tuning pass; "arid rains 4% of the time" does not, and a test carrying
/// the second teaches whoever is tuning to update the expectation without reading it - the same
/// argument <c>WeatherDistributionTests</c> makes about its own bands.
/// </remarks>
public sealed class ClimateTests
{
    private const string World = "ossara";

    private static double WetShare(string climate)
    {
        var wet = 0;

        for (var hour = 0; hour < GameInstant.HoursPerYear; hour++)
        {
            var state = WeatherOracle.At(World, climate, GameInstant.FromGameHours(hour)).State;

            if (state is not (WeatherState.Clear or WeatherState.Cloudy or WeatherState.Overcast
                or WeatherState.Fog))
            {
                wet++;
            }
        }

        return wet / (double)GameInstant.HoursPerYear;
    }

    private static double FrozenShare(string climate)
    {
        var frozen = 0;

        for (var hour = 0; hour < GameInstant.HoursPerYear; hour++)
        {
            var state = WeatherOracle.At(World, climate, GameInstant.FromGameHours(hour)).State;

            if (state is WeatherState.Snow or WeatherState.Blizzard)
            {
                frozen++;
            }
        }

        return frozen / (double)GameInstant.HoursPerYear;
    }

    [Fact]
    public void A_desert_is_drier_than_a_temperate_realm()
    {
        Assert.True(
            WetShare("arid") < WetShare("temperate"),
            "arid was not drier than temperate");
    }

    [Fact]
    public void A_coast_is_wetter_than_a_temperate_realm()
    {
        Assert.True(
            WetShare("coastal") > WetShare("temperate"),
            "coastal was not wetter than temperate");
    }

    [Fact]
    public void The_mountains_get_most_of_their_weather_as_snow()
    {
        // Alpine is the climate whose whole character is the temperature rather than the moisture:
        // it is no wetter than temperate, and almost everything that falls out of it is frozen.
        var alpine = FrozenShare("alpine");

        Assert.True(alpine > FrozenShare("temperate"), "alpine did not snow more than temperate");
        Assert.True(alpine > WetShare("alpine") / 2, "most of alpine's weather was not snow");
    }

    [Fact]
    public void Nothing_falls_underground()
    {
        // How a realm with no sky says so without every room in it carrying `indoors`. This is the
        // strongest claim in the file and it is a hard zero: not "rarely", never.
        Assert.Equal(0, WetShare("subterranean"));
    }

    [Fact]
    public void Underground_is_the_same_weather_all_year()
    {
        var states = Enumerable
            .Range(0, GameInstant.HoursPerYear)
            .Select(h => WeatherOracle.At(World, "subterranean", GameInstant.FromGameHours(h)).State)
            .Distinct()
            .ToList();

        Assert.Equal([WeatherState.Clear], states);
    }

    [Fact]
    public void A_blighted_realm_has_its_seasons_the_wrong_way_round()
    {
        // The one profile that is not a real climate. Somewhere that has come loose from the year
        // should read as loose to anybody watching over a few real weeks, without a line of prose
        // saying so - so midwinter is the warm half.
        var midsummer = WeatherOracle.At(
            World, "blighted", GameInstant.FromGameHours(GameInstant.HoursPerYear * 0.375));

        var midwinter = WeatherOracle.At(
            World, "blighted", GameInstant.FromGameHours(GameInstant.HoursPerYear * 0.875));

        Assert.True(
            midwinter.Temperature > midsummer.Temperature,
            $"blighted midwinter {midwinter.Temperature:F1} was not warmer than "
            + $"midsummer {midsummer.Temperature:F1}");
    }

    [Fact]
    public void The_climate_changes_the_sky_and_the_world_key_still_changes_the_timing()
    {
        // Two realms on the same climate must not share weather - the seed is the world key, and
        // that is what stops one storm crossing all five at once.
        var differences = 0;

        for (var hour = 0; hour < GameInstant.HoursPerYear; hour += 3)
        {
            var when = GameInstant.FromGameHours(hour);

            if (WeatherOracle.At("ossara", "coastal", when).State
                != WeatherOracle.At("grask", "coastal", when).State)
            {
                differences++;
            }
        }

        Assert.True(differences > 100, $"only {differences} sampled hours differed");
    }
}

using Muwbta.Domain.Characters;
using Muwbta.Domain.Weather;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// The sky as a player meets it: in a <c>look</c>, in a line while they are standing there, and
/// through the <c>indoors</c> flag that decides whether they get either.
/// </summary>
/// <remarks>
/// <b>Nothing here asserts on a mechanical effect, because there are none by decision</b>
/// (docs/WEATHER.md §6). Weather reaches every outdoor room in every world at once, so a number
/// attached to it is a world-wide balance change on a schedule nobody controls. What these tests
/// defend is that it is <em>said</em>, said to the right people, and not said to anyone under a
/// roof.
/// </remarks>
public sealed class WeatherTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>Advances until the weather actually changes, and returns what everyone was told.</summary>
    /// <remarks>
    /// <para>
    /// Written as a search rather than as a fixed number of hours because the weather is a smooth
    /// function of the clock: there is no hour at which it is guaranteed to turn, and a test that
    /// picked one would be asserting on the noise seed rather than on the system. The search is
    /// bounded, and a world that went a whole game year without its weather changing would be a
    /// finding in itself.
    /// </para>
    /// <para>
    /// Two game days is more than enough in practice - the fronts run on a fourteen-hour period -
    /// but the bound is generous so this cannot become a flaky test if the noise is ever retuned.
    /// </para>
    /// </remarks>
    private static string AdvanceUntilTheSkyChanges(WorldHarness harness, PlayerActorDrain drain)
    {
        for (var step = 0; step < GameInstant.HoursPerYear; step++)
        {
            harness.AdvanceSky(1);
            var said = drain();

            if (said.Length > 0)
            {
                return said;
            }
        }

        return string.Empty;
    }

    private delegate string PlayerActorDrain();

    // -----------------------------------------------------------------------
    // What a look carries
    // -----------------------------------------------------------------------

    [Fact]
    public void Looking_outdoors_says_what_the_sky_is_doing()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "look");

        var expected = WeatherNarration.Standing(harness.Weather.StateOf("test"));
        Assert.Contains(expected, harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Looking_indoors_does_not()
    {
        // The whole job of the flag. It has been in the registry since Phase 2 with no reader at
        // all (BUGS.md #19); this is the test that says it has one.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetRoomFlag(West, RoomFlags.Indoors.Key, true));

        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "look");

        var outdoorLine = WeatherNarration.Standing(harness.Weather.StateOf("test"));
        Assert.DoesNotContain(outdoorLine, harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void The_flag_one_scope_up_shelters_the_whole_zone()
    {
        // How an inn, a keep or an underground zone is actually authored: once, not room by room.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetZoneFlag("test.zone", RoomFlags.Indoors.Key, true));

        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "look");

        var outdoorLine = WeatherNarration.Standing(harness.Weather.StateOf("test"));
        Assert.DoesNotContain(outdoorLine, harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dark_room_says_nothing_about_the_sky_either()
    {
        // Somebody who cannot see the room cannot see what is over it. A weather line under
        // DarkProse would be the game describing the clouds to a player feeling for the wall.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetRoomFlag(West, RoomFlags.Dark.Key, true));

        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "look");

        var said = harness.DrainText(player);
        Assert.Contains(Engine.Presentation.PlayerView.DarkProse, said, StringComparison.Ordinal);
        Assert.DoesNotContain(
            WeatherNarration.Standing(harness.Weather.StateOf("test")),
            said,
            StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // The transition, which is the point of the feature
    // -----------------------------------------------------------------------

    [Fact]
    public void The_first_reading_announces_nothing()
    {
        // A player who arrives during a downpour is told it is raining by their look. Being told
        // it has just started, when it has been raining for hours, is the version that lies.
        var harness = Loaded();
        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.PrimeSky();

        Assert.Equal(string.Empty, harness.DrainText(player));
    }

    [Fact]
    public void A_change_in_the_sky_is_narrated_to_people_standing_outdoors()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Kael", West);
        harness.PrimeSky();
        harness.Drain(player);

        var said = AdvanceUntilTheSkyChanges(harness, () => harness.DrainText(player));

        Assert.NotEqual(string.Empty, said);
    }

    [Fact]
    public void It_describes_the_change_rather_than_restating_the_state()
    {
        // What "a meaningful emote" has to mean: the line a player reads is about something
        // happening, not a status field being printed at them. A transition line is never the
        // same sentence as the standing line for the state it arrives at.
        foreach (var to in Enum.GetValues<WeatherState>())
        {
            foreach (var from in Enum.GetValues<WeatherState>())
            {
                if (from == to)
                {
                    continue;
                }

                var line = WeatherNarration.Transition(from, to, GameInstant.FromGameHours(7));

                Assert.NotNull(line);
                Assert.NotEqual(WeatherNarration.Standing(to), line);
            }
        }
    }

    [Theory]
    [InlineData(WeatherState.Rain, WeatherState.Snow, false)]
    [InlineData(WeatherState.Snow, WeatherState.Rain, true)]
    [InlineData(WeatherState.Storm, WeatherState.Blizzard, false)]
    [InlineData(WeatherState.Blizzard, WeatherState.Storm, true)]
    public void A_freeze_or_a_thaw_is_narrated_as_one(
        WeatherState from,
        WeatherState to,
        bool warming)
    {
        // Rain and snow are the same weather at two temperatures, so they share a rung on the
        // severity ladder and neither direction is an arrival or a clearing. Sent down either of
        // those tables the line came out backwards: a freeze announced itself as "the blizzard
        // settles into ordinary snowfall" while it had been raining a moment earlier.
        //
        // Every hour, so both variants of each line are covered rather than whichever one the
        // hour picked - the point is that no wording in the table describes the wrong direction.
        string[] warmWords = ["warm", "rain", "soften"];
        string[] coldWords = ["cold", "snow", "white"];
        var expected = warming ? warmWords : coldWords;

        for (var hour = 0; hour < 6; hour++)
        {
            var line = WeatherNarration.Transition(from, to, GameInstant.FromGameHours(hour));

            Assert.NotNull(line);
            Assert.True(
                expected.Any(w => line.Contains(w, StringComparison.OrdinalIgnoreCase)),
                $"{from} to {to} at hour {hour} read \"{line}\"");
        }
    }

    [Fact]
    public void Nothing_is_narrated_to_somebody_indoors()
    {
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetRoomFlag(Middle, RoomFlags.Indoors.Key, true));

        var outside = harness.AddPlayer("Kael", West);
        var inside = harness.AddPlayer("Ilse", Middle);

        harness.PrimeSky();
        harness.Drain(outside);
        harness.Drain(inside);

        var heardOutside = AdvanceUntilTheSkyChanges(harness, () => harness.DrainText(outside));

        Assert.NotEqual(string.Empty, heardOutside);
        Assert.Equal(string.Empty, harness.DrainText(inside));
    }

    [Fact]
    public void Nothing_is_narrated_to_a_sleeper()
    {
        // Same answer as indoors, for the same reason: neither of them is watching the sky. It is
        // the rule WorldState.AwakeIn already applies to everything that is only seen.
        var harness = Loaded();
        harness.MakePeaceful(West);

        var awake = harness.AddPlayer("Kael", West);
        var asleep = harness.AddPlayer("Ilse", West);

        harness.Execute(asleep, "sleep");
        Assert.Equal(CharacterRestState.Sleep, asleep.Character.RestState);

        harness.PrimeSky();
        harness.Drain(awake);
        harness.Drain(asleep);

        var heard = AdvanceUntilTheSkyChanges(harness, () => harness.DrainText(awake));

        Assert.NotEqual(string.Empty, heard);
        Assert.Equal(string.Empty, harness.DrainText(asleep));
    }

    // -----------------------------------------------------------------------
    // Climate
    // -----------------------------------------------------------------------

    [Fact]
    public void A_world_with_no_climate_gets_the_temperate_one()
    {
        var harness = Loaded();
        harness.PrimeSky();

        Assert.Equal(
            WeatherOracle.At("test", "temperate", harness.Weather.Now).State,
            harness.Weather.StateOf("test"));
    }

    [Fact]
    public void The_climate_flag_on_the_world_changes_the_sky()
    {
        // The whole point of the text flag: content decides what kind of weather a realm has.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetWorldFlag("test", RoomFlags.Climate.Key, "subterranean"));
        harness.PrimeSky();

        // Underground has no weather at all, which is the one climate with an absolute answer.
        Assert.Equal(WeatherState.Clear, harness.Weather.StateOf("test"));

        var quiet = true;

        for (var hour = 0; hour < 500 && quiet; hour++)
        {
            harness.AdvanceSky(1);
            quiet = harness.Weather.StateOf("test") is WeatherState.Clear;
        }

        Assert.True(quiet, "the weather changed in a world that has no sky");
    }

    [Fact]
    public void A_zone_can_declare_its_own_climate_without_changing_the_realms()
    {
        // Weather is per-world, so a zone's climate changes what a *room* resolves and nothing
        // about the front crossing the realm. Stated as a test because the asymmetry is the kind
        // of thing somebody would otherwise call a bug.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetZoneFlag("test.zone", RoomFlags.Climate.Key, "alpine"));

        Assert.Equal("alpine", harness.World.TextFlag(West, RoomFlags.Climate));
        Assert.Equal("temperate", harness.World.WorldTextFlag("test", RoomFlags.Climate));
    }

    // -----------------------------------------------------------------------
    // sky
    // -----------------------------------------------------------------------

    [Fact]
    public void Sky_reports_the_season_the_hour_and_the_weather()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "sky");
        var said = harness.DrainText(player);

        Assert.Contains(harness.Weather.Now.Season.ToString(), said, StringComparison.Ordinal);
        Assert.Contains(
            WeatherNarration.Standing(harness.Weather.StateOf("test")),
            said,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Sky_indoors_still_tells_the_time_and_says_why_there_is_no_weather()
    {
        // Losing sight of the sky is not losing track of the day. A player in a cellar knows
        // roughly what hour it is; what they have lost is the view, and they are told that.
        var harness = Loaded();
        harness.Mutate(new Engine.Mutations.SetRoomFlag(West, RoomFlags.Indoors.Key, true));

        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "sky");
        var said = harness.DrainText(player);

        Assert.Contains(harness.Weather.Now.Season.ToString(), said, StringComparison.Ordinal);
        Assert.Contains("cannot see the sky", said, StringComparison.Ordinal);
    }

    [Fact]
    public void Sky_is_reachable_on_two_letters()
    {
        // `sk` was picked because it was unclaimed: a thing typed idly should be cheap, and
        // `weather` would have needed four characters to clear `wear` (§13's namespace argument).
        var harness = Loaded();
        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        harness.Execute(player, "sk");

        Assert.Contains("It is ", harness.DrainText(player), StringComparison.Ordinal);
    }
}

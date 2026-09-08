using Muwbta.Domain.Weather;
using Muwbta.Domain.Worlds;

namespace Muwbta.Domain.Tests.Worlds;

/// <summary>
/// The first flag whose value is a word rather than a yes (PLAN.md §4.10, docs/WEATHER.md §1).
/// </summary>
/// <remarks>
/// Two things are being defended here and they pull in opposite directions. A text flag has to
/// inherit exactly as a boolean does, because that chain is the whole reason it is a flag at all
/// rather than a column. And it has to be <em>closed</em> - a value the registry does not list is
/// a typo, and the one thing it must never do is resolve to itself and quietly change the weather.
/// </remarks>
public sealed class TextFlagTests
{
    private static FlagSet Climate(string value)
    {
        var set = new FlagSet();
        set.Set(RoomFlags.Climate.Key, value);
        return set;
    }

    [Fact]
    public void The_nearest_level_that_declares_it_wins()
    {
        var resolved = RoomFlags.ResolveText(
            RoomFlags.Climate, Climate("alpine"), Climate("arid"), Climate("coastal"));

        Assert.Equal("alpine", resolved.Value);
        Assert.Equal(FlagSource.Room, resolved.Source);
    }

    [Fact]
    public void A_level_that_says_nothing_falls_through()
    {
        var resolved = RoomFlags.ResolveText(RoomFlags.Climate, null, null, Climate("arid"));

        Assert.Equal("arid", resolved.Value);
        Assert.Equal(FlagSource.World, resolved.Source);
        Assert.True(resolved.IsInherited);
    }

    [Fact]
    public void Nothing_anywhere_is_the_registry_default()
    {
        var resolved = RoomFlags.ResolveText(RoomFlags.Climate, null, null, null);

        Assert.Equal("temperate", resolved.Value);
        Assert.Equal(FlagSource.Default, resolved.Source);
    }

    [Fact]
    public void A_value_the_registry_does_not_list_falls_through()
    {
        // The API refuses an unknown choice on the way in, so reaching this means a hand-edited
        // bundle or a choice that has since been retired. Falling through is the same answer
        // §4.10 gives a mistyped key: the level above decides, and eventually the harmless default
        // does. Resolving to the typo would be a world with weather nobody can explain.
        var resolved = RoomFlags.ResolveText(
            RoomFlags.Climate, Climate("alpne"), null, Climate("arid"));

        Assert.Equal("arid", resolved.Value);
        Assert.Equal(FlagSource.World, resolved.Source);
    }

    [Fact]
    public void A_value_stored_as_the_wrong_kind_falls_through()
    {
        // `climate: true` is not a climate. Same rule the boolean side already has for
        // `pvp: "yes"`, and the same reason: a wrong-kinded value means this level did not
        // declare the flag.
        var wrongKind = new FlagSet();
        wrongKind.Set(RoomFlags.Climate.Key, true);

        var resolved = RoomFlags.ResolveText(RoomFlags.Climate, wrongKind, null, null);

        Assert.Equal("temperate", resolved.Value);
        Assert.Equal(FlagSource.Default, resolved.Source);
    }

    [Fact]
    public void A_boolean_flag_holding_a_word_is_still_not_set()
    {
        // The mirror of the case above, and the one that already held: the two resolvers agree
        // about what a wrong-kinded value means.
        var wrongKind = new FlagSet();
        wrongKind.Set(RoomFlags.Pvp.Key, "yes");

        Assert.False(RoomFlags.Resolve(RoomFlags.Pvp, wrongKind, null, null).Value);
    }

    [Fact]
    public void Every_climate_the_registry_offers_has_a_profile()
    {
        // The correspondence the profiles' own remarks promise is checked here rather than by the
        // type system, because the registry lives in Worlds and the numbers live in Weather, and a
        // reference from the registry into the weather model would be the wrong direction.
        //
        // Unknown names resolve to temperate by design, so this cannot assert "not temperate" for
        // every one - it asserts that each choice reaches a profile somebody wrote, which for the
        // five non-default ones means differing from the fallback.
        foreach (var choice in RoomFlags.Climate.Choices.Where(c => c != "temperate"))
        {
            Assert.True(
                Climates.For(choice) != Climates.Temperate,
                $"'{choice}' is offered by the registry and has no profile of its own");
        }
    }

    [Fact]
    public void The_default_choice_is_the_one_the_profiles_fall_back_to()
    {
        Assert.Equal("temperate", RoomFlags.Climate.DefaultText);
        Assert.Equal(Climates.Temperate, Climates.For(RoomFlags.Climate.DefaultText));
        Assert.Equal(Climates.Temperate, Climates.For(null));
    }
}

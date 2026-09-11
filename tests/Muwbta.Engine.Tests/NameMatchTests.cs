using Muwbta.Engine;

namespace Muwbta.Engine.Tests;

/// <summary>
/// What counts as naming a thing.
/// </summary>
/// <remarks>
/// Playtesting note: "mobs and items need alias name list so that you don't have to type the
/// exact name". Matching is derived from the name rather than authored, so a builder who writes
/// "old coin" gets "coin", "old", and "co" without maintaining a keyword list.
/// </remarks>
public sealed class NameMatchTests
{
    private sealed record Thing(string? Name, string Key);

    private static Thing? Best(IEnumerable<Thing> things, string typed) =>
        NameMatch.Best(things, typed, t => t.Name, t => t.Key);

    [Theory]
    [InlineData("old coin")]   // the full display name
    [InlineData("old-coin")]   // the template key
    [InlineData("coin")]       // the noun, which is what a player actually types
    [InlineData("Coin")]       // case is irrelevant
    [InlineData("old")]        // the adjective still identifies it
    [InlineData("co")]         // a prefix of the noun
    public void An_old_coin_answers_to(string typed)
    {
        Assert.True(NameMatch.Matches(typed, "old coin", "old-coin"));
    }

    /// <summary>
    /// A plural reaches the singular it names. Quests ask for "eight crew tags" and the item is
    /// "a crew tag"; playtesting found <c>give tags</c> failing on every multi-item quest there is.
    /// </summary>
    [Theory]
    [InlineData("tags", "a crew tag")]
    [InlineData("markers", "a fallen road marker")]
    [InlineData("pages", "a page of the daily office")]
    [InlineData("leaves", "a leaf from the deep ledger")]
    [InlineData("knives", "a skinning knife")]
    [InlineData("boxes", "a tinder box")]
    [InlineData("berries", "a bramble berry")]
    [InlineData("TAGS", "a crew tag")]
    public void A_plural_answers_for_its_singular(string typed, string name)
    {
        Assert.True(NameMatch.Matches(typed, name, key: null));
    }

    /// <summary>
    /// The plural is a fallback, so a name that really does end in s is found as typed and
    /// ranks as it always did.
    /// </summary>
    [Fact]
    public void A_name_that_ends_in_s_is_still_matched_as_typed()
    {
        var things = new[]
        {
            new Thing("a pair of quiet wraps", "wraps"),
            new Thing("a wrap", "wrap"),
        };

        Assert.Equal("wraps", Best(things, "wraps")?.Key);
    }

    /// <summary>And a plural never turns into a match on a word the name does not have.</summary>
    [Theory]
    [InlineData("glass", "a glass lens")]
    [InlineData("cogs", "a drive cog")]
    public void Names_a_word_of_counts_whole_words_and_plurals(string typed, string name)
    {
        Assert.True(NameMatch.NamesAWordOf(typed, name));
    }

    [Theory]
    [InlineData("gold", "the first mark")]
    [InlineData("dri", "a drive cog")]
    [InlineData("stones", "a fallen road marker")]
    public void Names_a_word_of_refuses_prefixes_and_strangers(string typed, string name)
    {
        Assert.False(NameMatch.NamesAWordOf(typed, name));
    }

    [Theory]
    [InlineData("crown")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(null)]
    public void An_old_coin_does_not_answer_to(string? typed)
    {
        Assert.False(NameMatch.Matches(typed, "old coin", "old-coin"));
    }

    [Fact]
    public void An_exact_name_beats_a_word_match_elsewhere()
    {
        // Typing the whole thing must be unambiguous, even when another item shares the word.
        var things = new[]
        {
            new Thing("old coin purse", "coin-purse"),
            new Thing("old coin", "old-coin"),
        };

        Assert.Equal("old-coin", Best(things, "old coin")?.Key);
    }

    /// <summary>
    /// The last word is the noun in an English noun phrase, so "dagger" should reach the dagger
    /// rather than the dagger hilt - the hilt is a kind of hilt, not a kind of dagger.
    /// </summary>
    [Fact]
    public void The_noun_wins_over_a_word_buried_earlier_in_another_name()
    {
        var things = new[]
        {
            new Thing("dagger hilt", "dagger-hilt"),
            new Thing("rusty dagger", "rusty-dagger"),
        };

        Assert.Equal("rusty-dagger", Best(things, "dagger")?.Key);
    }

    [Fact]
    public void A_key_match_is_found_when_there_is_no_display_name()
    {
        // Mobs spawned before names were populated carry an empty TemplateName.
        var things = new[] { new Thing(null, "giant-rat") };

        Assert.Equal("giant-rat", Best(things, "rat")?.Key);
    }

    [Fact]
    public void A_hyphenated_key_and_a_spaced_name_answer_to_the_same_words()
    {
        Assert.True(NameMatch.Matches("sentry", null, "kobold-sentry"));
        Assert.True(NameMatch.Matches("sentry", "kobold sentry", "k1"));
    }

    /// <summary>
    /// The old mob lookup was <c>TemplateKey.EndsWith(typed)</c>, which accepted any trailing
    /// fragment: "t" matched "giant-rat", and so did "ant-rat". A player typing a stray letter
    /// got a fight rather than "you don't see that here".
    /// </summary>
    [Theory]
    [InlineData("t")]
    [InlineData("ant-rat")]
    [InlineData("nt")]
    public void A_trailing_fragment_is_no_longer_a_match(string typed)
    {
        Assert.False(NameMatch.Matches(typed, "giant rat", "giant-rat"));
    }

    [Fact]
    public void A_prefix_of_a_word_still_matches()
    {
        // The useful half of what EndsWith was doing, kept: "gia" reaches the giant rat.
        Assert.True(NameMatch.Matches("gia", "giant rat", "giant-rat"));
        Assert.True(NameMatch.Matches("ra", "giant rat", "giant-rat"));
    }

    [Fact]
    public void Nothing_matches_in_an_empty_room()
    {
        Assert.Null(Best([], "coin"));
    }

    [Fact]
    public void A_tie_keeps_the_first_candidate()
    {
        // Two identical things: room order is the only sensible tiebreak, and it must be stable
        // so repeating a command twice does not act on a different one each time.
        var things = new[]
        {
            new Thing("old coin", "old-coin-a"),
            new Thing("old coin", "old-coin-b"),
        };

        Assert.Equal("old-coin-a", Best(things, "coin")?.Key);
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.True(NameMatch.Matches("  coin  ", "old coin", "old-coin"));
    }
}

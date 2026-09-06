using Muwbta.Server.Assist;

namespace Muwbta.Server.Tests.Assist;

/// <summary>
/// The canon prefix: how a configuration's text is normalised for the model, what leads when
/// there is none, and that the document the Reaches' canon is written in still fits the window.
/// </summary>
public sealed class CanonTests
{
    /// <summary>
    /// What the prefix may occupy of the 16,384-token window. The rest, from
    /// <c>Modelfile.builder</c>: ~950 for the schema, ~700 for the zone's exemplars, ~600 to
    /// generate a room, and headroom.
    /// </summary>
    private const int PrefixTokenBudget = 12_000;

    /// <summary>
    /// The budget above is the model's, and the setting the panel and the validator measure
    /// against must agree with it - two numbers for one window is how the panel says "fits"
    /// while the model stops reading.
    /// </summary>
    [Fact]
    public void The_budget_here_is_the_setting_the_panel_uses()
    {
        Assert.Equal(PrefixTokenBudget, AssistOptions.DefaultCanonTokenBudget);
        Assert.Equal(PrefixTokenBudget, new AssistOptions().CanonTokenBudget);
    }

    // The Reaches' canon used to be checked here, read straight out of docs/WORLD.md. That
    // document went with the world it describes, and the claim generalised on the way out:
    // BundleValidator warns when any configuration carries a canon over the assist's budget, so
    // every bundle is now held to what only one document was. The failure it guards is unchanged -
    // an over-long prompt is truncated rather than refused, so the model reads as though it had
    // learned the world and forgotten most of it - and the answer is still which section has
    // stopped being canon, not how to raise the number.


    /// <summary>The sandbox's canon is a register and a map, not a theology.</summary>
    [Fact]
    public void The_starter_canon_is_small()
    {
        var tokens = Canon.EstimateTokens(Canon.Resolve(Muwbta.Persistence.Seeding.StarterWorldSeeder.StarterCanon));

        Assert.InRange(tokens, 200, 1_500);
    }

    /// <summary>
    /// A configuration with no canon is told so, rather than told about somebody else's world.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    public void No_canon_resolves_to_nothing_and_the_prompt_leads_with_saying_so(string? live)
    {
        Assert.Equal(string.Empty, Canon.Resolve(live));
        Assert.Same(Canon.None, Canon.ForPrompt(live));
        Assert.DoesNotContain("Reaches", Canon.None, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live canon is normalised - line endings, trailing whitespace, one final newline - because
    /// the cache is byte-exact and a textarea is not; and cut at the marker, so pasting the whole
    /// document works.
    /// </summary>
    [Fact]
    public void A_live_canon_is_normalised_and_cut_at_the_marker()
    {
        var pasted = "# Elsewhere\r\n\r\nA different world.  \r\n\r\n<!-- canon:end -->\r\n## Notes\r\n";

        Assert.Equal("# Elsewhere\n\nA different world.\n", Canon.Resolve(pasted));
        Assert.Equal("# Elsewhere\n\nA different world.\n", Canon.ForPrompt(pasted));
        Assert.DoesNotContain('\r', Canon.Resolve(pasted));
    }

    /// <summary>Resolving twice is resolving once: a stored canon does not drift on re-save.</summary>
    [Fact]
    public void Resolving_is_idempotent()
    {
        var once = Canon.Resolve("# A\r\n\r\nB.  \r\n");

        Assert.Equal(once, Canon.Resolve(once));
    }
}

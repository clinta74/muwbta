using Muwbta.Domain.Accounts;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Handing coin over: <c>give 50 gold Steve</c>, and the builder verb that makes some.
/// </summary>
/// <remarks>
/// Gold had no way of moving between two people at all. It arrived from a corpse or a sale and
/// left through a shop, which made every price in the game a price paid to the world rather than
/// to anyone in it - no lending, no splitting a find, no paying somebody to come along.
///
/// The assertions worth reading are the ones where the two purses still sum to what they started
/// with. Everything else here is parsing - how far the verb reaches is
/// <see cref="GiveReachTests"/>, which covers coin and goods together because the rule belongs to
/// <c>give</c> rather than to either form of it.
/// </remarks>
public sealed class GiveGoldTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static PlayerActor Carrying(
        WorldHarness harness,
        string name,
        long gold,
        AccountRole role = AccountRole.Player)
    {
        var actor = harness.AddPlayer(name, Room, role);
        actor.Character.Gold = gold;
        return actor;
    }

    // -----------------------------------------------------------------------
    // Player to player
    // -----------------------------------------------------------------------

    [Fact]
    public void Coin_moves_from_one_purse_to_the_other()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        var mira = Carrying(harness, "Mira", 5);
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold Mira");

        Assert.Equal(50, kael.Character.Gold);
        Assert.Equal(55, mira.Character.Gold);
    }

    [Fact]
    public void Both_sides_are_told_the_amount()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        var mira = Carrying(harness, "Mira", 0);
        harness.Drain(kael);
        harness.Drain(mira);

        harness.Execute(kael, "give 50 gold Mira");

        Assert.Contains("You give 50 gold to Mira", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Contains("Kael gives you 50 gold", harness.DrainText(mira), StringComparison.Ordinal);
    }

    /// <summary>
    /// The room sees the exchange without seeing the figure.
    /// </summary>
    /// <remarks>
    /// Both parties know the amount and neither chose to announce it. A room that reads out every
    /// purse passing through it makes standing in one a hazard, and a bystander only needs to know
    /// that coin changed hands.
    /// </remarks>
    [Fact]
    public void The_room_is_not_told_how_much()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        Carrying(harness, "Mira", 0);
        var onlooker = Carrying(harness, "Bren", 0);
        harness.Drain(onlooker);

        harness.Execute(kael, "give 50 gold Mira");

        var seen = harness.DrainText(onlooker);
        Assert.Contains("hands Mira some coins", seen, StringComparison.Ordinal);
        Assert.DoesNotContain("50", seen, StringComparison.Ordinal);
    }

    [Fact]
    public void The_word_to_is_allowed_between_the_amount_and_the_name()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        var mira = Carrying(harness, "Mira", 0);

        harness.Execute(kael, "give 50 gold to Mira");

        Assert.Equal(50, mira.Character.Gold);
    }

    [Fact]
    public void Gold_is_case_insensitive()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        var mira = Carrying(harness, "Mira", 0);

        harness.Execute(kael, "give 50 GOLD Mira");

        Assert.Equal(50, mira.Character.Gold);
    }

    // -----------------------------------------------------------------------
    // What it refuses
    // -----------------------------------------------------------------------

    [Fact]
    public void You_cannot_give_what_you_do_not_have()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 10);
        var mira = Carrying(harness, "Mira", 0);
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold Mira");

        Assert.Equal(10, kael.Character.Gold);
        Assert.Equal(0, mira.Character.Gold);
        Assert.Contains("You have 10", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// A negative amount is a theft written as a gift, so it is refused rather than negated.
    /// </summary>
    [Fact]
    public void A_negative_amount_moves_nothing()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        var mira = Carrying(harness, "Mira", 100);
        harness.Drain(kael);

        harness.Execute(kael, "give -50 gold Mira");

        Assert.Equal(100, kael.Character.Gold);
        Assert.Equal(100, mira.Character.Gold);
        Assert.Contains("Give how much?", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_moves_nothing()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        Carrying(harness, "Mira", 0);
        harness.Drain(kael);

        harness.Execute(kael, "give 0 gold Mira");

        Assert.Equal(100, kael.Character.Gold);
        Assert.Contains("Give how much?", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_amount_with_nobody_named_asks_who()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold");

        Assert.Equal(100, kael.Character.Gold);
        Assert.Contains("to whom?", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void You_cannot_give_gold_to_yourself()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold Kael");

        Assert.Equal(100, kael.Character.Gold);
        Assert.Contains("yourself", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_nobody_answers_to_moves_nothing()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold Steve");

        Assert.Equal(100, kael.Character.Gold);
        Assert.Contains("no one named Steve here", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Mobs
    // -----------------------------------------------------------------------

    /// <summary>
    /// A mob takes coin, and then it is carrying it: the amount joins the gold its killer splits.
    /// </summary>
    [Fact]
    public void A_mob_carries_what_it_is_handed()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 100);
        var man = harness.AddMob("oldman", Room, name: "old man");
        man.ResolvedGold = 7;
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold old man");

        Assert.Equal(50, kael.Character.Gold);
        Assert.Equal(57, man.ResolvedGold);
        Assert.Contains("You give 50 gold to the old man", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // The item form is untouched
    // -----------------------------------------------------------------------

    /// <summary>
    /// The coin reading is tried first, so this pins that it declines everything that is not one.
    /// </summary>
    [Fact]
    public void Giving_an_item_still_works()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var mira = harness.AddPlayer("Mira", Room);
        harness.GiveItem(kael, harness.DefineItem("rusted-blade", "rusted blade", null));

        harness.Execute(kael, "give blade Mira");

        Assert.Single(harness.World.InventoryOf(mira.Character.Id));
    }

    // -----------------------------------------------------------------------
    // spawn gold
    // -----------------------------------------------------------------------

    [Fact]
    public void A_builder_can_spawn_gold()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 5, role: AccountRole.Builder);
        harness.Drain(kael);

        harness.Execute(kael, "spawn gold 100");

        Assert.Equal(105, kael.Character.Gold);
        Assert.Contains("Spawned: 100 gold", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Spawned_gold_can_then_be_handed_over()
    {
        // The pair, which is the point of putting it in the builder's own purse rather than
        // straight into somebody else's.
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 0, role: AccountRole.Builder);
        var mira = Carrying(harness, "Mira", 0);

        harness.Execute(kael, "spawn gold 100");
        harness.Execute(kael, "give 100 gold Mira");

        Assert.Equal(0, kael.Character.Gold);
        Assert.Equal(100, mira.Character.Gold);
    }

    [Theory]
    [InlineData("spawn gold nine")]
    [InlineData("spawn gold -100")]
    [InlineData("spawn gold 0")]
    public void Spawning_a_nonsense_amount_makes_nothing(string input)
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 5, role: AccountRole.Builder);
        harness.Drain(kael);

        harness.Execute(kael, input);

        Assert.Equal(5, kael.Character.Gold);
        Assert.Contains("spawn gold <amount>", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// A player cannot conjure money: <c>spawn</c> is builder-gated as a whole.
    /// </summary>
    [Fact]
    public void A_player_cannot_spawn_gold()
    {
        var harness = Loaded();
        var kael = Carrying(harness, "Kael", 5);
        harness.Drain(kael);

        harness.Execute(kael, "spawn gold 100");

        Assert.Equal(5, kael.Character.Gold);
    }
}

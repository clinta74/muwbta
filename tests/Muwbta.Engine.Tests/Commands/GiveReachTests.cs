using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// How far <c>give</c> reaches: exactly as far as the room, for coin and for goods alike.
/// </summary>
/// <remarks>
/// The item half did not check at all. It resolved its recipient with <c>FindPlayerByName</c>,
/// which searches everyone online, and then never asked where they were - so <c>give blade Mira</c>
/// handed the blade over with Mira in another room, another zone, or another world. Nothing about
/// the exchange was local except the fiction of it, and a market that reaches every player at once
/// is a different economy from the one the rooms describe.
///
/// The coin half was written with the check and is here beside it, because the rule belongs to
/// <c>give</c> rather than to either form of it: one class fails when the two halves drift apart.
/// </remarks>
public sealed class GiveReachTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static readonly RoomKey Elsewhere = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static PlayerActor Holding(WorldHarness harness, string name, string itemName)
    {
        var actor = harness.AddPlayer(name, Room);
        harness.GiveItem(actor, harness.DefineItem(itemName.Replace(' ', '-'), itemName, null));
        return actor;
    }

    // -----------------------------------------------------------------------
    // Goods
    // -----------------------------------------------------------------------

    [Fact]
    public void An_item_does_not_reach_another_room()
    {
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "rusted blade");
        var mira = harness.AddPlayer("Mira", Elsewhere);
        harness.Drain(kael);

        harness.Execute(kael, "give blade Mira");

        Assert.Single(harness.World.InventoryOf(kael.Character.Id));
        Assert.Empty(harness.World.InventoryOf(mira.Character.Id));
        Assert.Contains("Mira is not here.", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_still_reaches_somebody_standing_here()
    {
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "rusted blade");
        var mira = harness.AddPlayer("Mira", Room);

        harness.Execute(kael, "give blade Mira");

        Assert.Empty(harness.World.InventoryOf(kael.Character.Id));
        Assert.Single(harness.World.InventoryOf(mira.Character.Id));
    }

    /// <summary>
    /// The split still names the person it could not reach.
    /// </summary>
    /// <remarks>
    /// <c>SplitGive</c> decides where the item name ends and the recipient begins by trying every
    /// split and taking the first where both halves resolve - and "resolve" now means present, so
    /// no split resolves at all here. The fallback has to leave "Mira" as the recipient rather
    /// than swallowing her into a two-word item name, or the complaint is about the wrong thing.
    /// </remarks>
    [Fact]
    public void A_two_word_item_still_names_the_recipient_it_could_not_reach()
    {
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "empty glass");
        harness.AddPlayer("Mira", Elsewhere);
        harness.Drain(kael);

        harness.Execute(kael, "give empty glass Mira");

        Assert.Contains("Mira is not here.", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// A mob you can see beats a player you cannot.
    /// </summary>
    /// <remarks>
    /// The room is asked before the rest of the world is, so somebody three zones away does not
    /// get to answer for the thing standing in front of you. Both halves of <c>give</c> order it
    /// this way, which is why both are pinned here.
    /// </remarks>
    [Fact]
    public void A_mob_here_answers_before_a_player_who_is_elsewhere()
    {
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "rusted blade");
        harness.AddPlayer("Mira", Elsewhere);
        harness.AddMob("mira", Room, name: "Mira");
        harness.Drain(kael);

        harness.Execute(kael, "give blade Mira");

        var text = harness.DrainText(kael);
        Assert.Contains("has no use for", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is not here", text, StringComparison.Ordinal);
        Assert.Single(harness.World.InventoryOf(kael.Character.Id));
    }

    [Fact]
    public void Nobody_of_that_name_anywhere_is_still_told_so()
    {
        var harness = Loaded();
        var kael = Holding(harness, "Kael", "rusted blade");
        harness.Drain(kael);

        harness.Execute(kael, "give blade Steve");

        Assert.Contains("no one named Steve here", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Coin
    // -----------------------------------------------------------------------

    [Fact]
    public void Coin_does_not_reach_another_room()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;
        var mira = harness.AddPlayer("Mira", Elsewhere);
        harness.Drain(kael);

        harness.Execute(kael, "give 50 gold Mira");

        Assert.Equal(100, kael.Character.Gold);
        Assert.Equal(0, mira.Character.Gold);
        Assert.Contains("Mira is not here.", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Coin_reaches_a_mob_here_before_a_player_who_is_elsewhere()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;
        var mira = harness.AddPlayer("Mira", Elsewhere);
        var double_ = harness.AddMob("mira", Room, name: "Mira");

        harness.Execute(kael, "give 50 gold Mira");

        Assert.Equal(50, kael.Character.Gold);
        Assert.Equal(0, mira.Character.Gold);
        Assert.Equal(50, double_.ResolvedGold);
    }
}

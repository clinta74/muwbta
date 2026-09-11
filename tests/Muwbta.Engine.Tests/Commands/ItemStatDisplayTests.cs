using Muwbta.Domain.Abilities;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// The three screens that show a player what their gear does, from one playtest session.
/// </summary>
/// <remarks>
/// <para>
/// All three notes were the same bug: the player was being shown the builder's view of an item, or
/// no view at all. <c>stats</c> printed the raw stat bag; <c>inventory</c> listed only the slots
/// that happened to be filled; a shop listed a name and a price and nothing else, and since
/// <c>examine</c> reaches your pack, the floor and the people in the room — and shop stock is a
/// template that has never been spawned — buying it was the only way to find out what it did.
/// </para>
/// <para>
/// The wording itself is <c>ItemStatLine</c>'s to get right and is tested there. These are about
/// the three screens asking it.
/// </para>
/// </remarks>
public sealed class ItemStatDisplayTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static (WorldHarness Harness, PlayerActor Player) Ready()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        return (harness, harness.AddPlayer("Bram", Room, level: 10));
    }

    private static string Run(WorldHarness harness, PlayerActor actor, string command)
    {
        harness.Drain(actor);
        harness.Execute(actor, command);

        return harness.DrainText(actor);
    }

    /// <summary>A shopkeeper stocking the given templates, authored the way the database delivers it.</summary>
    private static void AddShopkeeper(WorldHarness harness, params string[] sells) =>
        harness.AddMob(
            "barkeep",
            Room,
            name: "barkeep",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["type"] = "npc",
                ["sells"] = new List<object>(sells),
            }));

    // -----------------------------------------------------------------------
    // inventory: every slot, filled or not
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The empty lines are how the slot list is learned.</b> A player wearing six things had no
    /// way to discover that Feet and Trinket exist — a whole equipment category findable only by
    /// happening to pick one up.
    /// </summary>
    [Fact]
    public void Inventory_names_every_slot_including_the_empty_ones()
    {
        var (harness, player) = Ready();

        var said = Run(harness, player, "inventory");

        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            Assert.Contains($"[{slot}]", said, StringComparison.Ordinal);
        }

        Assert.Contains("empty", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filled_slot_shows_the_item_rather_than_empty()
    {
        var (harness, player) = Ready();
        var helm = harness.DefineItem("helm", "a helm of vision", ItemSlot.Head);
        harness.Equip(player, helm, ItemSlot.Head);

        var said = Run(harness, player, "inventory");

        Assert.Contains("[Head] a helm of vision", said, StringComparison.Ordinal);
        Assert.Contains("[Feet] ", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pack listing is where somebody deciding what to keep is actually looking, so it carries
    /// the numbers too.
    /// </summary>
    [Fact]
    public void Inventory_says_what_a_worn_item_does()
    {
        var (harness, player) = Ready();
        var blade = harness.DefineWeapon(
            "blade", "a keening blade", ItemSlot.MainHand, 12, "slash",
            damageMin: 4, damageMax: 8, attackBonus: 5);
        harness.Equip(player, blade, ItemSlot.MainHand);

        var said = Run(harness, player, "inventory");

        Assert.Contains("Damage 4-8, +5 to hit", said, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // stats: words, not keys
    // -----------------------------------------------------------------------

    /// <summary>
    /// It read <c>bonus=4, damageMax=13, damageMin=7</c> — alphabetical, so the maximum came
    /// before the minimum, and the one term that is accuracy was named only by its authored key.
    /// </summary>
    [Fact]
    public void Stats_names_the_terms_rather_than_the_keys()
    {
        var (harness, player) = Ready();
        var maul = harness.DefineWeapon(
            "maul", "a measured oathmaul", ItemSlot.MainHand, 16, "crush",
            damageMin: 7, damageMax: 13, attackBonus: 4);
        harness.Equip(player, maul, ItemSlot.MainHand);

        var said = Run(harness, player, "stats");

        Assert.Contains("Damage 7-13, +4 to hit", said, StringComparison.Ordinal);
        Assert.DoesNotContain("damageMin", said, StringComparison.Ordinal);
        Assert.DoesNotContain("bonus=", said, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // list: what it is, and whether it beats yours
    // -----------------------------------------------------------------------

    [Fact]
    public void A_shop_says_what_a_weapon_does_and_where_it_goes()
    {
        var (harness, player) = Ready();
        harness.DefineWeapon(
            "maul", "a measured oathmaul", ItemSlot.MainHand, 16, "crush",
            damageMin: 7, damageMax: 13, attackBonus: 4);
        AddShopkeeper(harness, "maul");

        var said = Run(harness, player, "list");

        Assert.Contains("main hand", said, StringComparison.Ordinal);
        Assert.Contains("Damage 7-13, +4 to hit", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The note itself: <em>no way to know if an item is better than the one you have without
    /// buying it</em>. Answered as what moves, in both directions, rather than as a verdict.
    /// </summary>
    [Fact]
    public void A_shop_compares_its_stock_against_what_you_are_wearing()
    {
        var (harness, player) = Ready();

        var worn = harness.DefineWeapon(
            "blade", "a keening blade", ItemSlot.MainHand, 12, "slash",
            damageMin: 4, damageMax: 8, attackBonus: 5);
        harness.Equip(player, worn, ItemSlot.MainHand);

        harness.DefineWeapon(
            "maul", "a measured oathmaul", ItemSlot.MainHand, 16, "crush",
            damageMin: 7, damageMax: 13, attackBonus: 4);
        AddShopkeeper(harness, "maul");

        var said = Run(harness, player, "list");

        Assert.Contains("Against your a keening blade: +4 damage, -1 to hit.", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shop_says_when_you_have_nothing_in_that_slot()
    {
        var (harness, player) = Ready();
        harness.DefineWeapon(
            "maul", "a measured oathmaul", ItemSlot.MainHand, 16, "crush",
            damageMin: 7, damageMax: 13, attackBonus: 4);
        AddShopkeeper(harness, "maul");

        var said = Run(harness, player, "list");

        Assert.Contains("You have nothing there.", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rope, a lamp, a loaf: the price is the whole story, and a blank line under every one of
    /// them is noise on a listing that may be twenty items long.
    /// </summary>
    [Fact]
    public void A_shop_stays_quiet_about_an_item_with_no_numbers_and_no_slot()
    {
        var (harness, player) = Ready();
        harness.DefineItem("rope", "a coil of rope", slot: null, value: 5);
        AddShopkeeper(harness, "rope");

        var said = Run(harness, player, "list");

        Assert.Contains("a coil of rope", said, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing there", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Against your", said, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // draughts: what drinking one does
    // -----------------------------------------------------------------------

    private static Domain.Items.ItemTemplate Tonic(WorldHarness harness)
    {
        var tonic = harness.DefineItem("tonic", "a stamina tonic", slot: null, drinkValue: 1);
        tonic.UseEffects =
        [
            new AbilityEffectSpec("resource.restore", new Dictionary<string, string>
            {
                ["resource"] = "Stamina",
                ["amount"] = "25",
            }),
        ];
        return tonic;
    }

    [Fact]
    public void Examining_a_draught_says_what_drinking_it_does()
    {
        var (harness, player) = Ready();
        harness.GiveItem(player, Tonic(harness));

        var said = Run(harness, player, "examine tonic");

        Assert.Contains("Drinking it restores 25 stamina to you", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shop_says_what_a_draught_does()
    {
        var (harness, player) = Ready();
        Tonic(harness);
        AddShopkeeper(harness, "tonic");

        var said = Run(harness, player, "list");

        Assert.Contains("drink: restores 25 stamina to you", said, StringComparison.Ordinal);
    }
}

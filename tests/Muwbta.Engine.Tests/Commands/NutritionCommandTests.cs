using System.Globalization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// <c>eat</c> and <c>drink</c> — the first verbs in the game that take an item away for good.
/// </summary>
/// <remarks>
/// The guards matter more than the happy path, for the reason <c>DestroyCommandTests</c> gives: a
/// consuming verb has no way back, so what it refuses is the interesting half. These two inherit
/// destroy's guards deliberately rather than growing their own.
/// </remarks>
public sealed class NutritionCommandTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void Eating_food_answers_hunger_and_consumes_the_loaf()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var loaf = harness.DefineItem("bread", "a loaf", slot: null, foodValue: 40);
        harness.GiveItem(kael, loaf);

        kael.Character.Vitals.Hunger = 60;

        harness.Execute(kael, "eat bread");

        Assert.Equal(20, kael.Character.Vitals.Hunger);
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
    }

    [Fact]
    public void Drinking_answers_thirst_and_not_hunger()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var skin = harness.DefineItem("waterskin", "a waterskin", slot: null, drinkValue: 50);
        harness.GiveItem(kael, skin);

        kael.Character.Vitals.Hunger = 60;
        kael.Character.Vitals.Thirst = 60;

        harness.Execute(kael, "drink waterskin");

        Assert.Equal(10, kael.Character.Vitals.Thirst);
        Assert.Equal(60, kael.Character.Vitals.Hunger);
    }

    /// <summary>Food is not drink, however hungry it would have made you.</summary>
    [Fact]
    public void You_cannot_drink_a_loaf()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var loaf = harness.DefineItem("bread", "a loaf", slot: null, foodValue: 40);
        harness.GiveItem(kael, loaf);
        kael.Character.Vitals.Thirst = 60;

        harness.Execute(kael, "drink bread");

        Assert.Equal(60, kael.Character.Vitals.Thirst);
        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
    }

    /// <summary>An ordinary item is not a meal, and is not eaten trying.</summary>
    [Fact]
    public void An_item_that_is_not_food_is_refused_and_kept()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var rope = harness.DefineItem("rope", "a coil of rope", slot: null);
        harness.GiveItem(kael, rope);

        harness.Execute(kael, "eat rope");

        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
    }

    /// <summary>
    /// A full character keeps the loaf rather than wasting it.
    /// </summary>
    /// <remarks>
    /// Taking the item and giving nothing back reads as a bug even when it is the rule — the player
    /// cannot see the number they are already at.
    /// </remarks>
    [Fact]
    public void Eating_while_full_wastes_nothing()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var loaf = harness.DefineItem("bread", "a loaf", slot: null, foodValue: 40);
        harness.GiveItem(kael, loaf);

        kael.Character.Vitals.Hunger = 0;

        harness.Execute(kael, "eat bread");

        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
    }

    /// <summary>Eating never takes a character past full.</summary>
    [Fact]
    public void A_big_meal_stops_at_full()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var feast = harness.DefineItem("feast", "a feast", slot: null, foodValue: 90);
        harness.GiveItem(kael, feast);

        kael.Character.Vitals.Hunger = 20;

        harness.Execute(kael, "eat feast");

        Assert.Equal(0, kael.Character.Vitals.Hunger);
    }

    /// <summary>The item leaves storage as well as the world, or the next load hands it back.</summary>
    [Fact]
    public void An_eaten_item_is_deleted_from_storage_too()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var loaf = harness.DefineItem("bread", "a loaf", slot: null, foodValue: 40);
        var instance = harness.GiveItem(kael, loaf);

        kael.Character.Vitals.Hunger = 50;
        harness.Execute(kael, "eat bread");

        // Without this the row outlives the meal and the loaf returns on the next load.
        Assert.Equal([instance.Id], harness.ItemSaves.Deleted);
    }

    /// <summary>Worn food comes off first — destroy's guard, inherited.</summary>
    [Fact]
    public void Something_worn_has_to_come_off_first()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var pendant = harness.DefineItem(
            "sugar-pendant", "a sugar pendant", ItemSlot.Trinket, foodValue: 20);
        var instance = harness.GiveItem(kael, pendant);
        instance.EquippedSlot = ItemSlot.Trinket;

        kael.Character.Vitals.Hunger = 50;
        harness.Execute(kael, "eat sugar-pendant");

        Assert.Equal(50, kael.Character.Vitals.Hunger);
        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
    }

    /// <summary>Both verbs are reachable at their own abbreviation, and neither shadows a direction.</summary>
    [Fact]
    public void The_verbs_do_not_collide_with_the_directions()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);

        harness.Execute(kael, "eat");
        Assert.Contains("Eat what?", harness.DrainText(kael), StringComparison.Ordinal);

        harness.Execute(kael, "dri");
        Assert.Contains("Drink what?", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Potions: drinks with effects
    // -----------------------------------------------------------------------

    private static ItemTemplate Tonic(WorldHarness harness, int amount = 25, int? cooldown = 40)
    {
        var template = harness.DefineItem("tonic", "a stamina tonic", slot: null, drinkValue: 1);
        template.UseEffects =
        [
            new AbilityEffectSpec("resource.restore", new Dictionary<string, string>
            {
                ["resource"] = "Stamina",
                ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
            }),
        ];
        template.UseCooldownPulses = cooldown;
        return template;
    }

    /// <summary>A draught does what it says whether or not you are thirsty, and says how much.</summary>
    [Fact]
    public void A_draught_restores_what_it_says_even_when_not_thirsty()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, Tonic(harness));
        kael.Character.Vitals.Stamina = 10;
        kael.Character.Vitals.Thirst = 0;

        harness.Execute(kael, "drink tonic");

        Assert.Equal(35, kael.Character.Vitals.Stamina);
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
        Assert.Contains("+25 stamina", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>Every draught shares one timer, and the second waits for it.</summary>
    [Fact]
    public void A_second_draught_waits_for_the_first()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var tonic = Tonic(harness, cooldown: 40);
        harness.GiveItem(kael, tonic);
        harness.GiveItem(kael, tonic);
        kael.Character.Vitals.Stamina = 10;

        harness.Execute(kael, "drink tonic");
        harness.Drain(kael);
        harness.Execute(kael, "drink tonic");

        Assert.Contains("more second", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
        Assert.Equal(35, kael.Character.Vitals.Stamina);

        harness.Clock.AdvancePulses(40);
        harness.Execute(kael, "drink tonic");

        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
        Assert.Equal(60, kael.Character.Vitals.Stamina);
    }

    /// <summary>Refused rather than wasted, as a drink to somebody not thirsty is.</summary>
    [Fact]
    public void A_draught_is_not_wasted_on_someone_at_their_best()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, Tonic(harness));
        kael.Character.Vitals.Thirst = 0;

        harness.Execute(kael, "drink tonic");

        Assert.Contains("already at your best", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
    }

    [Fact]
    public void Quaff_drinks()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, Tonic(harness));
        kael.Character.Vitals.Stamina = 10;

        harness.Execute(kael, "quaff tonic");

        Assert.Equal(35, kael.Character.Vitals.Stamina);
    }
}

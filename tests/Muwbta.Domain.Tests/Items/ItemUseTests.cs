using Muwbta.Domain.Abilities;
using Muwbta.Domain.Items;

namespace Muwbta.Domain.Tests.Items;

/// <summary>
/// What a thing you can eat or drink says about itself on <c>examine</c>.
/// </summary>
/// <remarks>
/// From playtesting: the draughts and tonics examined as "It isn't something you can wear or
/// wield" and nothing else.
/// </remarks>
public sealed class ItemUseTests
{
    private static AbilityEffectSpec Restore(string resource, string amount) =>
        new("resource.restore", new Dictionary<string, string> { ["resource"] = resource, ["amount"] = amount });

    private static ItemTemplate Consumable(int? food = null, int? drink = null, int? cooldown = null, params AbilityEffectSpec[] effects) => new()
    {
        Key = "thing",
        Name = "a thing",
        Icon = "!",
        FoodValue = food,
        DrinkValue = drink,
        UseCooldownPulses = cooldown,
        UseEffects = [.. effects],
    };

    [Fact]
    public void A_draught_says_what_drinking_it_does_and_how_long_until_another()
    {
        var line = ItemUse.Describe(Consumable(drink: 5, effects: Restore("Stamina", "25")));

        Assert.Equal(
            "Drinking it restores 25 stamina to you, and nothing else like it will go down for 30 seconds after.",
            line);
    }

    [Fact]
    public void A_proportional_heal_is_named_as_the_share_it_is()
    {
        var line = ItemUse.Describe(Consumable(
            drink: 5,
            effects: new AbilityEffectSpec("heal.restore", new Dictionary<string, string> { ["healPercent"] = "35" })));

        Assert.StartsWith("Drinking it restores 35", line, StringComparison.Ordinal);
        Assert.Contains("of maximum health to you", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_effects_are_joined_and_its_own_wait_is_the_one_quoted()
    {
        var line = ItemUse.Describe(Consumable(
            drink: 5, cooldown: 40, effects: [Restore("Stamina", "25"), Restore("Focus", "10")]));

        Assert.Equal(
            "Drinking it restores 25 stamina to you and restores 10 focus to you, and nothing else like it will go down for 10 seconds after.",
            line);
    }

    [Fact]
    public void Food_with_an_effect_is_eaten()
    {
        Assert.StartsWith("Eating it", ItemUse.Describe(Consumable(food: 10, effects: Restore("Stamina", "5"))), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, null, "You could eat it.")]
    [InlineData(null, 10, "You could drink it.")]
    [InlineData(10, 10, "You could eat or drink it.")]
    public void Plain_food_and_drink_say_only_which_they_are(int? food, int? drink, string expected)
    {
        Assert.Equal(expected, ItemUse.Describe(Consumable(food, drink)));
    }

    [Fact]
    public void Something_you_cannot_eat_or_drink_says_nothing()
    {
        Assert.Null(ItemUse.Describe(Consumable()));
        Assert.Null(ItemUse.Describe(null));
    }
}

using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;

namespace Muwbta.Domain.Items;

/// <summary>
/// The rule for what an item may do when it is eaten or drunk (<see cref="ItemTemplate.UseEffects"/>).
/// </summary>
/// <remarks>
/// One place, asked by the builder API on save and by the bundle validator on import, so the two
/// cannot come to disagree about which effects a potion may carry.
/// </remarks>
public static class ItemUse
{
    private static readonly EffectRegistry Effects = new();

    /// <summary>
    /// What eating or drinking this does, in the words the effects use for themselves - "restores
    /// 35% of maximum health to you" - or null when it does nothing but fill a need.
    /// </summary>
    /// <remarks>
    /// The effects' own <c>Describe</c>, which is what the ability listing prints, so a draught and
    /// a heal that do the same thing are described the same way and cannot drift apart.
    /// </remarks>
    public static string? EffectsProse(ItemTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var phrases = template.UseEffects
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .Select(e => Effects.Get(e.Key)?.Describe(e.Params ?? [], TargetingType.Self, casterLevel: 1))
            .OfType<string>()
            .ToList();

        return phrases.Count switch
        {
            0 => null,
            1 => phrases[0],
            _ => string.Join(", ", phrases[..^1]) + " and " + phrases[^1],
        };
    }

    /// <summary>
    /// The line <c>examine</c> shows for something that can be eaten or drunk, or null when it
    /// cannot be.
    /// </summary>
    /// <remarks>
    /// From playtesting: a draught examined as "It isn't something you can wear or wield" and
    /// nothing else, so the only way to learn what one did was to drink it. The wait is part of the
    /// answer, because every consumable with effects shares it and a player planning a fight needs
    /// to know they get one.
    /// </remarks>
    public static string? Describe(ItemTemplate? template)
    {
        if (template is null)
        {
            return null;
        }

        var food = template.FoodValue is > 0;
        var drink = template.DrinkValue is > 0;

        if (!food && !drink)
        {
            return null;
        }

        if (EffectsProse(template) is not { } effects)
        {
            return (food, drink) switch
            {
                (true, true) => "You could eat or drink it.",
                (true, false) => "You could eat it.",
                _ => "You could drink it.",
            };
        }

        var verb = drink ? "Drinking" : "Eating";
        var seconds = (int)Math.Ceiling(
            (template.UseCooldownPulses ?? ItemTemplate.DefaultUseCooldownPulses) * AbilityAudience.SecondsPerPulse);

        return seconds > 0
            ? $"{verb} it {effects}, and nothing else like it will go down for {seconds} seconds after."
            : $"{verb} it {effects}.";
    }

    /// <summary>What is wrong with this effect on a consumable, or null when nothing is.</summary>
    public static string? Problem(AbilityEffectSpec effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (string.IsNullOrWhiteSpace(effect.Key))
        {
            return "Every effect on an item needs a key.";
        }

        return Effects.Get(effect.Key) switch
        {
            null => $"'{effect.Key}' is not an effect this server knows.",
            { IsHarmful: true } => $"'{effect.Key}' is harmful, and something eaten or drunk has nobody to hurt.",
            _ => null,
        };
    }
}

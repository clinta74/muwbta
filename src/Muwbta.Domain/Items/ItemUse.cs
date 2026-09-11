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

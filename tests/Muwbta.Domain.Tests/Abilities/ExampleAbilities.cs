using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// A small ability set for the tests that are about <see cref="AbilityProgression"/> rather than
/// about the game's content.
/// </summary>
/// <remarks>
/// <para>
/// These tests used to run against <c>AbilityCatalogue</c>, which was the shipped set and made a
/// convenient fixture — until the set moved to <c>shipped/abilities.json</c> and the catalogue
/// became four examples. Nothing about "does level 3 grant what level 3 unlocks" was ever a
/// question about content, so pointing them at a fixture is what they should have done anyway:
/// a logic test that fails when a designer retunes an unlock level is a test that reports the
/// wrong thing.
/// </para>
/// <para>
/// The keys and levels are the ones those assertions already named, so the fixture reads like the
/// Warden line it stands in for: an opener at 1, something heavier at 3, a buff at 5, a debuff at
/// 7, and one thing far enough up the range to prove granting continues.
/// </para>
/// </remarks>
internal static class ExampleAbilities
{
    internal static IReadOnlyList<Ability> Set { get; } =
    [
        One(CharacterPath.Warden, 1, "warden.kick"),
        One(CharacterPath.Warden, 3, "warden.bash"),
        One(CharacterPath.Warden, 5, "warden.battle-fury"),
        One(CharacterPath.Warden, 7, "warden.sunder"),
        One(CharacterPath.Warden, 20, "warden.last-stand"),
        One(CharacterPath.Adept, 1, "adept.bolt"),
        One(CharacterPath.Temper, 1, "temper.strike"),
        One(CharacterPath.Hallow, 1, "hallow.mend"),
    ];

    private static Ability One(CharacterPath path, int level, string key) => new()
    {
        Key = key,
        Path = path,
        UnlockLevel = level,
        Name = key,
        Description = string.Empty,
        CostType = CostType.Stamina,
        CostValue = 10,
        CooldownPulses = 24,
        TargetingType = TargetingType.SingleTarget,
        Effects = [new AbilityEffectSpec("damage.physical", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["scalingFactor"] = "1.1",
            ["minDamage"] = "3",
        })],
    };
}

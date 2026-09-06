using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// The four examples a fresh database is seeded with.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file used to hold twenty-three guards over the game's whole ability set.</b> The set is
/// <c>shipped/abilities.json</c> now and the guards went with it, to
/// <c>AbilityContentTests</c> — every one of them was written after the thing it forbids had
/// already shipped, so none of them was dropped and none of them was weakened. What is asserted
/// here is what the catalogue still is: a floor, so a brand-new database has something castable on
/// every Path before anybody imports anything.
/// </para>
/// <para>
/// The set-level questions those guards ask — does a Path keep unlocking to twenty, are the gaps
/// playable — are deliberately not asked here. Four level-1 abilities fail all of them, and should.
/// </para>
/// </remarks>
public sealed class AbilityCatalogueTests
{
    private static readonly CharacterPath[] Paths =
        [CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Temper, CharacterPath.Hallow];

    private static readonly EffectRegistry KnownEffects = new();

    /// <summary>
    /// Every Path has something castable at level 1, which is the entire job.
    /// </summary>
    /// <remarks>
    /// A character created on a Path with nothing seeded for it can log in, level up, and never
    /// find out that the reason they have no abilities is an empty table rather than their level.
    /// </remarks>
    [Fact]
    public void Every_path_can_do_something_on_a_database_with_nothing_imported()
    {
        foreach (var path in Paths)
        {
            Assert.Contains(AbilityCatalogue.For(path), e => e.UnlockLevel == 1);
        }
    }

    [Fact]
    public void The_examples_are_examples_rather_than_a_progression()
    {
        // Four, one each. If this grows, the question to ask is whether the addition belongs in
        // content instead - which it almost certainly does.
        Assert.Equal(Paths.Length, AbilityCatalogue.All.Count);
        Assert.All(AbilityCatalogue.All, e => Assert.Equal(1, e.UnlockLevel));
    }

    [Fact]
    public void Every_ability_key_is_unique()
    {
        var duplicates = AbilityCatalogue.All
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// The key carries the Path in front of it, which is load-bearing rather than tidy:
    /// <c>AbilityLookup</c> resolves a key as well as a name, so a key naming the wrong Path is a
    /// name that resolves to somebody else's ability.
    /// </summary>
    [Fact]
    public void An_ability_key_is_prefixed_with_the_path_that_learns_it()
    {
        foreach (var entry in AbilityCatalogue.All)
        {
            Assert.StartsWith(
                entry.Path.ToString().ToLowerInvariant() + ".", entry.Key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_example_names_an_effect_that_exists()
    {
        // An unknown effect key resolves to nothing and the cast silently does nothing.
        var unknown = AbilityCatalogue.AllEffects
            .Where(x => !KnownEffects.Contains(x.Effect.Key))
            .Select(x => $"{x.Entry.Key} -> {x.Effect.Key}")
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_example_costs_something()
    {
        // A free ability with a cooldown is a worse version of an auto-attack.
        Assert.All(AbilityCatalogue.All, e => Assert.True(e.CostValue > 0, $"{e.Key} is free."));
    }

    /// <summary>
    /// The progression grants exactly what the catalogue defines, in both directions.
    /// </summary>
    /// <remarks>
    /// The two lists were written out separately once and had drifted apart both ways: four
    /// abilities were unlocked with no row behind them, so levelling granted something uncastable,
    /// and three that were seeded appeared in no progression, so they were unlearnable. Both are
    /// derived from this list now, and this is what keeps that true.
    /// </remarks>
    [Fact]
    public void The_progression_and_the_catalogue_describe_the_same_abilities()
    {
        var defined = AbilityCatalogue.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var granted = Paths
            .SelectMany(p => AbilityProgression.GetAbilitiesForPath(AbilityCatalogue.AsAbilities, p))
            .Select(x => x.AbilityKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(defined, granted);
    }
}

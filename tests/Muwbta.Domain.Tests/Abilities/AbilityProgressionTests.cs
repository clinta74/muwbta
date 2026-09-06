using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// What a Path knows at a level, over <see cref="ExampleAbilities"/> rather than over content.
/// </summary>
/// <remarks>
/// These ran against <c>AbilityCatalogue</c> while it was the shipped set. It is four examples
/// now and the set is <c>shipped/abilities.json</c>, but the change these tests wanted was never
/// content anyway: "level 3 grants what level 3 unlocks" is a question about
/// <see cref="AbilityProgression"/>, and a logic test that fails when a designer retunes an
/// unlock level reports the wrong thing.
/// </remarks>
public sealed class AbilityProgressionTests
{
    [Fact]
    public void GetAbilitiesForPath_Warden_ReturnsWardenAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(ExampleAbilities.Set, CharacterPath.Warden);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("warden.", a.AbilityKey));
        Assert.Contains((1, "warden.kick"), abilities);
    }

    [Fact]
    public void GetAbilitiesForPath_Adept_ReturnsAdeptAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(ExampleAbilities.Set, CharacterPath.Adept);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("adept.", a.AbilityKey));
        Assert.Contains((1, "adept.bolt"), abilities);
    }

    [Fact]
    public void GetAbilitiesForPath_Temper_ReturnsBladeAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(ExampleAbilities.Set, CharacterPath.Temper);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("temper.", a.AbilityKey));
    }

    [Fact]
    public void GetAbilitiesForPath_Hallow_ReturnsHallowAbilities()
    {
        // Act
        var abilities = AbilityProgression.GetAbilitiesForPath(ExampleAbilities.Set, CharacterPath.Hallow);

        // Assert
        Assert.NotEmpty(abilities);
        Assert.All(abilities, a => Assert.StartsWith("hallow.", a.AbilityKey));
        Assert.Contains((1, "hallow.mend"), abilities);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_Level1_ReturnsLevel1Only()
    {
        // Act
        var known = AbilityProgression.GetKnownAbilitiesForLevel(ExampleAbilities.Set, CharacterPath.Warden, 1);

        // Assert
        Assert.Single(known);
        Assert.Contains("warden.kick", known);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_Level3_ReturnsLevel1And3()
    {
        // Act
        var known = AbilityProgression.GetKnownAbilitiesForLevel(ExampleAbilities.Set, CharacterPath.Warden, 3);

        // Assert
        Assert.Equal(2, known.Count);
        Assert.Contains("warden.kick", known);
        Assert.Contains("warden.bash", known);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_Level6_ReturnsEverythingUnlockedSoFar()
    {
        // Act
        var known = AbilityProgression.GetKnownAbilitiesForLevel(ExampleAbilities.Set, CharacterPath.Warden, 6);

        // Assert
        Assert.Equal(3, known.Count);
        Assert.Contains("warden.kick", known);
        Assert.Contains("warden.bash", known);

        // Battle Fury, not Parry. This used to expect parry at 6 and call the result "all",
        // which was doubly wrong: progression carried on past 6 in neither direction, and parry
        // had no ability row behind it, so the level-up granted something uncastable.
        Assert.Contains("warden.battle-fury", known);
        Assert.DoesNotContain("warden.sunder", known);
    }

    [Fact]
    public void GetKnownAbilitiesForLevel_KeepsGrantingPastLevelSix()
    {
        var atSix = AbilityProgression.GetKnownAbilitiesForLevel(ExampleAbilities.Set, CharacterPath.Warden, 6);
        var atTwenty = AbilityProgression.GetKnownAbilitiesForLevel(ExampleAbilities.Set, CharacterPath.Warden, 20);

        Assert.True(atTwenty.Count > atSix.Count);
    }

    [Fact]
    public void Knows_WithKnownAbility_ReturnsTrue()
    {
        // Act
        var knows = AbilityProgression.Knows(ExampleAbilities.Set, CharacterPath.Hallow, 1, "hallow.mend");

        // Assert
        Assert.True(knows);
    }

    [Fact]
    public void Knows_BelowUnlockLevel_ReturnsFalse()
    {
        // Act - Warden only gets bash at level 3
        var knows = AbilityProgression.Knows(ExampleAbilities.Set, CharacterPath.Warden, 2, "warden.bash");

        // Assert
        Assert.False(knows);
    }

    [Fact]
    public void A_character_knows_only_what_their_level_has_reached()
    {
        var atOne = AbilityProgression.GetKnownAbilitiesForLevel(
            ExampleAbilities.Set, CharacterPath.Warden, 1);
        var atSeven = AbilityProgression.GetKnownAbilitiesForLevel(
            ExampleAbilities.Set, CharacterPath.Warden, 7);

        Assert.Contains("warden.kick", atOne);
        Assert.DoesNotContain("warden.sunder", atOne);
        Assert.Contains("warden.sunder", atSeven);
    }

    [Fact]
    public void A_path_does_not_learn_another_paths_abilities()
    {
        var warden = AbilityProgression.GetKnownAbilitiesForLevel(
            ExampleAbilities.Set, CharacterPath.Warden, 20);

        Assert.DoesNotContain("adept.bolt", warden);
        Assert.DoesNotContain("temper.strike", warden);
    }

    [Fact]
    public void Knows_WithUnknownAbility_ReturnsFalse()
    {
        // Act
        var knows = AbilityProgression.Knows(ExampleAbilities.Set, CharacterPath.Warden, 10, "nonexistent.ability");

        // Assert
        Assert.False(knows);
    }
}

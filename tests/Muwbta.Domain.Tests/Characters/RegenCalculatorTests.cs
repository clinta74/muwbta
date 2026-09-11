using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Characters;

public sealed class RegenCalculatorTests
{
    [Fact]
    public void Calculate_sleep_baseline_attributes()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden); // Health 60, Focus 20, Stamina 100
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Sleep,
            vitals,
            vitalityModifier: 0,
            CharacterPath.Warden);

        // 35% of max per vital, rounded up
        Assert.Equal(21, health);  // 60 * 0.35
        Assert.Equal(7, focus);    // 20 * 0.35
        Assert.Equal(35, stamina); // 100 * 0.35
    }

    [Fact]
    public void Calculate_rest_baseline_attributes()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Rest,
            vitals,
            vitalityModifier: 0,
            CharacterPath.Warden);

        // 17% of max per vital, rounded up
        Assert.Equal(11, health);  // ceil(60 * 0.17) = ceil(10.2)
        Assert.Equal(4, focus);    // ceil(20 * 0.17) = ceil(3.4)
        Assert.Equal(17, stamina); // 100 * 0.17
    }

    [Fact]
    public void Calculate_stand_baseline_attributes()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Stand,
            vitals,
            vitalityModifier: 0,
            CharacterPath.Warden);

        // 2% of max per vital, rounded up
        Assert.Equal(2, health);   // ceil(60 * 0.02) = ceil(1.2)
        Assert.Equal(1, focus);    // ceil(20 * 0.02) = ceil(0.4)
        Assert.Equal(2, stamina);  // 100 * 0.02
    }

    [Fact]
    public void Calculate_positive_vitality_modifier_increases_regen()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var baseRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0,
            CharacterPath.Warden);
        var boostedRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 3, CharacterPath.Warden);

        // +3 modifier adds 3% to base 17% = 20% total
        Assert.Equal(12, boostedRegen.health); // 60 * 0.20
        Assert.True(boostedRegen.health > baseRegen.health);
    }

    [Fact]
    public void Calculate_negative_vitality_modifier_decreases_regen()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var baseRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0,
            CharacterPath.Warden);
        var penalizedRegen = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: -2, CharacterPath.Warden);

        // -2 modifier reduces base 17% by 2% = 15% total. 60 * 0.15 in doubles is a hair over 9,
        // and must not round up to 10.
        Assert.Equal(9, penalizedRegen.health);
        Assert.True(penalizedRegen.health < baseRegen.health);
    }

    [Fact]
    public void Calculate_minimum_is_one_per_vital()
    {
        var vitals = new Vitals
        {
            Health = 1,
            HealthMax = 1,
            Focus = 1,
            FocusMax = 1,
            Stamina = 1,
            StaminaMax = 1,
        };

        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Stand,
            vitals,
            vitalityModifier: 0,
            CharacterPath.Warden);

        // Even with 1 HP, regen is at least 1
        Assert.Equal(1, health);
        Assert.Equal(1, focus);
        Assert.Equal(1, stamina);
    }

    [Fact]
    public void Calculate_high_modifier_can_enable_regen_at_stand()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        // With +10 modifier, Stand is 2% + 10% = 12%
        var (health, focus, stamina) = RegenCalculator.Calculate(
            CharacterRestState.Stand,
            vitals,
            vitalityModifier: 10,
            CharacterPath.Warden);

        Assert.Equal(8, health); // ceil(60 * 0.12) = ceil(7.2)
        Assert.True(health > 2); // Significantly more than default 1
    }

    [Fact]
    public void ApplyRegen_increases_vitals_to_max()
    {
        var vitals = new Vitals
        {
            Health = 30,
            HealthMax = 60,
            Focus = 10,
            FocusMax = 20,
            Stamina = 40,
            StaminaMax = 100,
        };

        var changed = RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0,
            CharacterPath.Warden);

        Assert.True(changed);
        Assert.Equal(51, vitals.Health);    // 30 + 21 (35% of 60)
        Assert.Equal(17, vitals.Focus);     // 10 + 7 (35% of 20)
        Assert.Equal(75, vitals.Stamina);   // 40 + 35 (35% of 100)
    }

    [Fact]
    public void ApplyRegen_caps_to_max()
    {
        var vitals = new Vitals
        {
            Health = 55,
            HealthMax = 60,
            Focus = 18,
            FocusMax = 20,
            Stamina = 90,
            StaminaMax = 100,
        };

        RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0,
            CharacterPath.Warden);

        Assert.Equal(60, vitals.Health);   // 55 + 21, capped to 60
        Assert.Equal(20, vitals.Focus);    // 18 + 7, capped to 20
        Assert.Equal(100, vitals.Stamina); // 90 + 35, capped to 100
    }

    [Fact]
    public void ApplyRegen_returns_false_when_already_at_max()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        // Don't change anything; it's at max
        var changed = RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0,
            CharacterPath.Warden);

        Assert.False(changed);
    }

    [Fact]
    public void ApplyRegen_returns_true_even_if_one_vital_changes()
    {
        var vitals = new Vitals
        {
            Health = 59,       // Not at max
            HealthMax = 60,
            Focus = 40,        // At max
            FocusMax = 40,
            Stamina = 100,     // At max
            StaminaMax = 100,
        };

        var changed = RegenCalculator.ApplyRegen(CharacterRestState.Sleep, vitals, vitalityModifier: 0,
            CharacterPath.Warden);

        Assert.True(changed);
        Assert.Equal(60, vitals.Health);
    }

    [Fact]
    public void All_paths_regen_at_sleep()
    {
        var paths = new[] { CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Temper, CharacterPath.Hallow };

        foreach (var path in paths)
        {
            var vitals = Vitals.StartingFor(path);
            var (health, focus, stamina) = RegenCalculator.Calculate(
                CharacterRestState.Sleep,
                vitals,
                vitalityModifier: 0,
                path);

            Assert.True(health > 0);
            Assert.True(focus > 0);
            Assert.True(stamina > 0);
        }
    }

    [Fact]
    public void Sleep_regens_more_than_rest()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);
        var sleep = RegenCalculator.Calculate(CharacterRestState.Sleep, vitals, vitalityModifier: 0,
            CharacterPath.Warden);
        var rest = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0,
            CharacterPath.Warden);

        Assert.True(sleep.health > rest.health);
        Assert.True(sleep.focus > rest.focus);
        Assert.True(sleep.stamina > rest.stamina);
    }

    [Fact]
    public void Rest_regens_more_than_stand()
    {
        // Use Adept which has higher Focus max (50 vs 20), ensuring Rest > Stand for all vitals
        var vitals = Vitals.StartingFor(CharacterPath.Adept);
        var rest = RegenCalculator.Calculate(CharacterRestState.Rest, vitals, vitalityModifier: 0,
            CharacterPath.Adept);
        var stand = RegenCalculator.Calculate(CharacterRestState.Stand, vitals, vitalityModifier: 0,
            CharacterPath.Adept);

        Assert.True(rest.health > stand.health);
        Assert.True(rest.focus > stand.focus);
        Assert.True(rest.stamina > stand.stamina);
    }

    /// <summary>
    /// An empty bar fills in three ticks asleep and six resting, at every Path's starting size.
    /// </summary>
    /// <remarks>
    /// The promise the rates were tuned to keep, pinned directly rather than through the percentages,
    /// because rounding is what used to break it: a level-one Warden's twenty focus rested back at
    /// one a tick and took twenty.
    /// </remarks>
    [Theory]
    [InlineData(CharacterRestState.Sleep, 3)]
    [InlineData(CharacterRestState.Rest, 6)]
    public void An_empty_bar_fills_in_minutes(CharacterRestState state, int ticks)
    {
        foreach (var path in Enum.GetValues<CharacterPath>())
        {
            var vitals = Vitals.StartingFor(path);
            vitals.Health = 0;
            vitals.Focus = 0;
            vitals.Stamina = 0;

            for (var i = 0; i < ticks; i++)
            {
                RegenCalculator.ApplyRegen(state, vitals, vitalityModifier: 0, path);
            }

            Assert.Equal(vitals.HealthMax, vitals.Health);
            Assert.Equal(vitals.FocusMax, vitals.Focus);
            Assert.Equal(vitals.StaminaMax, vitals.Stamina);
        }
    }

    /// <summary>
    /// The two Paths that spend focus get it back twice as fast, in every state.
    /// </summary>
    /// <remarks>
    /// Compared at the same vitals rather than at each Path's own starting ones, so this measures
    /// the rate and not the fact that an Adept's focus pool is larger to begin with.
    /// </remarks>
    [Theory]
    [InlineData(CharacterRestState.Sleep)]
    [InlineData(CharacterRestState.Rest)]
    [InlineData(CharacterRestState.Stand)]
    public void Casters_recover_focus_twice_as_fast(CharacterRestState state)
    {
        Vitals Pool() => new()
        {
            Health = 0, HealthMax = 200,
            Focus = 0, FocusMax = 200,
            Stamina = 0, StaminaMax = 200,
        };

        var warden = RegenCalculator.Calculate(state, Pool(), vitalityModifier: 0, CharacterPath.Warden);
        var temper = RegenCalculator.Calculate(state, Pool(), vitalityModifier: 0, CharacterPath.Temper);
        var adept = RegenCalculator.Calculate(state, Pool(), vitalityModifier: 0, CharacterPath.Adept);
        var hallow = RegenCalculator.Calculate(state, Pool(), vitalityModifier: 0, CharacterPath.Hallow);

        Assert.Equal(warden.focus * 2, adept.focus);
        Assert.Equal(warden.focus * 2, hallow.focus);
        Assert.Equal(warden.focus, temper.focus);

        // Only focus. Health and stamina are the same for everyone, which is what keeps this from
        // being a blanket "casters recover faster" buff.
        Assert.Equal(warden.health, adept.health);
        Assert.Equal(warden.stamina, adept.stamina);
    }

    /// <summary>
    /// <c>HealthFor</c> is the health arm of <see cref="RegenCalculator.Calculate"/>, and has to stay
    /// that way.
    /// </summary>
    /// <remarks>
    /// It exists because mobs regenerate now and a mob has no Path, and the alternative was passing
    /// one that is not true — which <c>Calculate</c>'s own doc argues against. Splitting rather than
    /// faking only helps while the two still agree: two rate tables that drifted would mean a mob and
    /// a standing player healing at different speeds with nothing saying so, which is the same silent
    /// failure the split was meant to avoid. Across every state and both signs of modifier, because a
    /// divergence in one cell is all it takes.
    /// </remarks>
    [Theory]
    [InlineData(CharacterRestState.Sleep, 0)]
    [InlineData(CharacterRestState.Rest, 0)]
    [InlineData(CharacterRestState.Stand, 0)]
    [InlineData(CharacterRestState.Stand, 10)]
    [InlineData(CharacterRestState.Rest, -2)]
    public void HealthFor_agrees_with_Calculate(CharacterRestState state, int vitalityModifier)
    {
        var vitals = Vitals.StartingFor(CharacterPath.Warden);

        Assert.Equal(
            RegenCalculator.Calculate(state, vitals, vitalityModifier, CharacterPath.Warden).health,
            RegenCalculator.HealthFor(state, vitals, vitalityModifier));
    }

    /// <summary>
    /// And it does not consult the Path — which is the reason it can be the one a mob calls.
    /// </summary>
    [Fact]
    public void HealthFor_is_the_same_number_every_path_would_get()
    {
        var vitals = Vitals.StartingFor(CharacterPath.Adept);
        var expected = RegenCalculator.HealthFor(CharacterRestState.Rest, vitals, vitalityModifier: 0);

        foreach (var path in Enum.GetValues<CharacterPath>())
        {
            Assert.Equal(
                expected,
                RegenCalculator.Calculate(CharacterRestState.Rest, vitals, 0, path).health);
        }
    }
}

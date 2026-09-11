namespace Muwbta.Domain.Characters;

/// <summary>
/// Pure function: calculates vital regeneration based on rest state and attributes.
/// Scales with Vitality modifier to reward defensive gearing.
/// </summary>
public static class RegenCalculator
{
    /// <summary>
    /// Base regen percentage per vital per state, per tick. All vitals regen at the same rate
    /// for their state; different vitals' max values cause the absolute amounts to differ.
    /// </summary>
    /// <remarks>
    /// <b>Sleep fills an empty bar in three minutes; rest in six.</b> Raised from 15% and 8% after
    /// playtesting. Sleep is only allowed in a peaceful room, so it is already a trip back to safety,
    /// and making that trip then cost the better part of ten minutes was downtime with no decision in
    /// it. Rest stays at half the sleep rate, so lying down somewhere safe is still worth the walk.
    /// <b>Standing is left at 2%,</b> because it is also the rate a mob heals at between fights.
    /// </remarks>
    private static readonly Dictionary<CharacterRestState, double> BaseRegenPercent = new()
    {
        { CharacterRestState.Sleep, 0.35 },   // 35% of max per tick
        { CharacterRestState.Rest, 0.17 },    // 17% of max per tick
        { CharacterRestState.Stand, 0.02 },   // 2% of max per tick
    };

    /// <summary>
    /// How much faster a Path recovers focus than it recovers anything else.
    /// </summary>
    /// <remarks>
    /// Doubled for the two Paths that spend focus to do their job (PLAN.md §4.5). A Warden's focus
    /// bar is a small reserve behind a stamina bar that does the work; an Adept's <em>is</em> the
    /// work, and at the shared rate an empty one took the better part of an hour standing, which
    /// made the recovery verbs the game's most-typed ones for exactly two of the four Paths.
    ///
    /// Applied to the whole rate rather than only the base, so gearing Vitality still helps a caster
    /// recover twice as much as it helps a martial Path - the multiplier is about what the vital is
    /// <em>for</em>, not about which half of the sum it lands on.
    /// </remarks>
    public const double CasterFocusRate = 2.0;

    /// <summary>Whether this Path pays for its abilities in focus.</summary>
    public static double FocusRateFor(CharacterPath path) =>
        path is CharacterPath.Adept or CharacterPath.Hallow ? CasterFocusRate : 1.0;

    /// <summary>
    /// The health arm of <see cref="Calculate"/> on its own, for an entity that has no Path.
    /// </summary>
    /// <remarks>
    /// <b>Split out for mobs, and split rather than defaulted on purpose.</b> Mobs regenerate now
    /// (PLAN.md §4.6) and a mob has no <see cref="CharacterPath"/>, so the alternative was passing
    /// one that is not true — which <see cref="Calculate"/>'s own doc argues against, because the
    /// only symptom of the wrong Path is a rate nothing on screen reports. Health does not consult
    /// the Path at all, so there is nothing to invent: this is the whole of what a mob needs, and
    /// <see cref="Calculate"/> calls it for its own health term so the rate table stays one table.
    ///
    /// <b>Health only, for mobs.</b> Nothing reads a mob's focus or stamina — ability costs come off
    /// <c>character.Vitals</c> and combat only ever writes <c>mob.Vitals.Health</c> — and
    /// regenerating a bar with no readers is <c>itemPower</c> in miniature.
    /// </remarks>
    public static int HealthFor(CharacterRestState state, Vitals vitals, int vitalityModifier)
    {
        ArgumentNullException.ThrowIfNull(vitals);

        return Share(vitals.HealthMax, EffectivePercent(state, vitalityModifier, vitals));
    }

    /// <summary>A share of a maximum, rounded up, and never less than one.</summary>
    /// <remarks>
    /// <b>Rounded up, not down.</b> Flooring lost most of a point every tick on a small bar, and small
    /// bars are exactly the ones a new character has: a level-one Warden's twenty focus rested back at
    /// one a minute, twice as long as the rate said. The epsilon keeps floating-point noise from
    /// rounding a whole number up a further point: resting at a −2 Vitality modifier is
    /// 60 × (0.17 − 0.02), which comes out 9.000000000000002, not 9.
    /// </remarks>
    private static int Share(int maximum, double percent) =>
        Math.Max(1, (int)Math.Ceiling((maximum * percent) - 1e-9));

    /// <summary>The share of a maximum that comes back this tick, before any per-vital rate.</summary>
    /// <remarks>
    /// <b>Hunger and thirst are a multiplier on the whole rate, applied last.</b> Multiplied rather
    /// than subtracted so that neglecting food costs a resting character the same *share* it costs a
    /// standing one — a flat deduction would be a rounding error while asleep and the entire rate
    /// while standing, which is backwards: sleeping through a famine should not be the cure.
    ///
    /// <see cref="Needs.RegenShare"/> never returns zero, so this can slow recovery and never stop
    /// it. <c>RegenSystem</c> already skips anyone in combat, so the penalty only ever lengthens
    /// downtime; it makes food worth carrying without making a fight harder.
    /// </remarks>
    private static double EffectivePercent(CharacterRestState state, int vitalityModifier, Vitals? vitals = null) =>
        // Each modifier point adds one percentage point.
        (BaseRegenPercent[state] + (vitalityModifier * 0.01))
        * (vitals is null ? 1.0 : Needs.RegenShare(vitals.Hunger, vitals.Thirst));

    /// <summary>
    /// Calculate how much of each vital regenerates in a single 60-second tick.
    /// Amount is always at least 1 per vital, rounded up after applying modifiers.
    /// </summary>
    /// <remarks>
    /// Vitality modifier adds percentage points to the base regen rate. This ties recovery
    /// speed directly to character gearing and creates a tangible reward for stacking
    /// defensive attributes. A character with +3 Vitality modifier effectively gets 3% bonus
    /// regen across all states.
    ///
    /// The Path is taken rather than defaulted because getting it wrong is silent: a caster handed
    /// the martial rate recovers at half speed and nothing on screen says so.
    /// </remarks>
    public static (int health, int focus, int stamina) Calculate(
        CharacterRestState state,
        Vitals vitals,
        int vitalityModifier,
        CharacterPath path)
    {
        ArgumentNullException.ThrowIfNull(vitals);

        var effectivePercent = EffectivePercent(state, vitalityModifier, vitals);

        var health = HealthFor(state, vitals, vitalityModifier);
        var focus = Share(vitals.FocusMax, effectivePercent * FocusRateFor(path));
        var stamina = Share(vitals.StaminaMax, effectivePercent);

        return (health, focus, stamina);
    }

    /// <summary>
    /// Apply regen to a character's vitals in place, capping to max.
    /// Returns true if any vital changed, false if already at max.
    /// </summary>
    public static bool ApplyRegen(
        CharacterRestState state,
        Vitals vitals,
        int vitalityModifier,
        CharacterPath path)
    {
        var (healthRegen, focusRegen, staminaRegen) = Calculate(state, vitals, vitalityModifier, path);

        var beforeHealth = vitals.Health;
        var beforeFocus = vitals.Focus;
        var beforeStamina = vitals.Stamina;

        vitals.Health = Math.Min(vitals.Health + healthRegen, vitals.HealthMax);
        vitals.Focus = Math.Min(vitals.Focus + focusRegen, vitals.FocusMax);
        vitals.Stamina = Math.Min(vitals.Stamina + staminaRegen, vitals.StaminaMax);

        return vitals.Health != beforeHealth || vitals.Focus != beforeFocus || vitals.Stamina != beforeStamina;
    }
}

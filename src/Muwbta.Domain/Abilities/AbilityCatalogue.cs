using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Abilities;

/// <summary>
/// The ability set a <b>fresh database</b> is seeded with. Not the live one.
/// </summary>
/// <remarks>
/// <b>Read this before changing a number here.</b> The <c>abilities</c> table is the source of
/// truth; this list is what a database with no rows in it is born with, exactly as the twelve
/// Millbrook rooms in <c>StarterWorldSeeder</c> are. Its one reader in the whole of <c>src</c> is
/// that seeder, planting abilities a database does not already have. The Engine — the entire cast
/// path — never touches it.
///
/// <b>So editing a cooldown here does not retune the game.</b> It changes what a *new* install
/// starts with. Every database that already holds the row keeps its own value, because the
/// reconcile only inserts what is missing — which is deliberate, and is what stops a restart from
/// reverting a builder's work. A retune reaches an existing server as a migration or an imported
/// bundle, never by editing this file.
///
/// The list stays in code rather than becoming a data file because a fresh install has to get
/// abilities from somewhere before anything exists to import them from, and because it is the set
/// the tests hold the validator against.
///
/// Below: one list, rather than a seeder that writes ability rows and a progression table that names
/// them. Those were separate and had drifted in both directions: four abilities were unlocked at
/// level 6 with no row behind them (<c>warden.parry</c>, <c>adept.amplify</c>,
/// <c>temper.shadowstep</c>, <c>hallow.restore</c>) so reaching level 6 granted something that
/// could not be cast, while three that *were* seeded appeared in no progression at all
/// (<c>warden.battle-fury</c>, <c>adept.weaken</c>, <c>temper.fortify</c>) and so were unlearnable
/// - which is the whole of Phase 5.2a's buffs and debuffs, unreachable in play.
///
/// Deriving both from this list is what makes that class of mismatch impossible rather than
/// merely fixed.
///
/// Identity comes from cost, cadence, and scaling rather than from a private list of effects: a
/// Warden hits reliably and endures, a Temper pays little and strikes often, an Adept pays a lot for
/// a big slow hit, a Hallow mends more than it harms.
///
/// <b>Three of the four reach a whole room, and the Temper is the one that does not.</b> That used
/// to be two — an area ability is the strongest thing the executors can express, and spreading it
/// everywhere would cost every Path its shape. What changed is that the Warden's job past level 20
/// is holding a room rather than a target, and an area *taunt* is not an area *attack*: it buys no
/// damage and no survival, only the attention of everything present, which is the one thing that
/// Path exists to take. The Temper keeps none, because killing one thing properly is its shape.
/// </remarks>
public static class AbilityCatalogue
{
    /// <summary>One ability, and what it takes to learn it.</summary>
    /// <param name="Path">The Path that learns it.</param>
    /// <param name="UnlockLevel">The level at which it is granted.</param>
    /// <param name="Maintainable">
    /// Whether this ability is <em>meant</em> to be held up permanently — its duration may exceed
    /// its cooldown.
    /// </param>
    /// <remarks>
    /// <b>The general rule is that nothing outlasts its own cooldown</b>, because buffs refresh
    /// rather than stack, so a longer duration makes the cooldown do nothing. Ten of the eleven
    /// timed effects were in that state once and the retune that fixed it is why the rule exists.
    ///
    /// <b>Hallow's group buffs are the deliberate exception, and it is what makes the Path a
    /// buffer without making buffing its combat rotation.</b> The point of a long duration and a
    /// short cooldown together is that the group is set up <em>before</em> the fight and the buffs
    /// are still standing at the end of it — so the Hallow spends the fight healing, which is the
    /// other half of the job, instead of re-casting protection it already gave.
    ///
    /// That sets the duration floor: a maintainable buff must comfortably outlast a whole fight,
    /// or it becomes exactly the in-combat chore it exists to avoid. The short cooldown is so that
    /// setting up a group is not itself a chore.
    ///
    /// A self-buff held up forever is still free power, so this is granted to group protection and
    /// withheld from anything that raises damage or personal survival. It is catalogue metadata
    /// rather than a column: the <c>abilities</c> table has no such field and the engine has no
    /// opinion — it exists so the design rule can be tested against the shipped set, exactly as
    /// the combat-beat rule on cooldowns is.
    /// </remarks>
    public sealed record Entry(
        CharacterPath Path,
        int UnlockLevel,
        string Key,
        string Name,
        string Description,
        CostType CostType,
        int CostValue,
        long CooldownPulses,
        long? CastTimePulses,
        TargetingType TargetingType,
        List<AbilityEffectSpec> Effects,
        bool Maintainable = false,
        int? CooldownGroup = null);

    /// <summary>One effect, which is what all thirty-seven starter abilities have.</summary>
    /// <summary>One effect, which is what most starter abilities have.</summary>
    private static List<AbilityEffectSpec> Effect(string key, Dictionary<string, string> parameters) =>
        [new(key, parameters)];

    private static Dictionary<string, string> Damage(string scaling, string min) =>
        new(StringComparer.Ordinal) { ["scalingFactor"] = scaling, ["minDamage"] = min };

    private static Dictionary<string, string> Heal(string amount) =>
        new(StringComparer.Ordinal) { ["baseHeal"] = amount };

    /// <summary>
    /// Four abilities — one per Path, at level 1 — for a database with nothing in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This was the game's ability set, and is now an example of one.</b> Sixty-nine entries
    /// lived here, and the reason they moved is the reason worlds are not written in C# either:
    /// editing them retuned a fresh install and reached a running server not at all. The set is
    /// <c>shipped/abilities.json</c>, it travels through the same import as everything else, and
    /// <c>BundleValidator</c> checks it on the way in.
    /// </para>
    /// <para>
    /// What is left is the floor. The seeder plants these on first boot so a brand-new database
    /// has something castable on every Path, exactly as <c>StarterWorldSeeder</c> plants Millbrook
    /// so it has somewhere to stand. Neither is the game; both are enough to log in and confirm
    /// the server works before importing one.
    /// </para>
    /// <para>
    /// <b>The reconcile stays add-only</b>, which matters more now, not less: these four must
    /// never overwrite the imported set. A key that already exists is left exactly as it is,
    /// whether it got there from content or from a builder's editor.
    /// </para>
    /// <para>
    /// The design reasoning that used to sit against each entry — why the Warden opens with a kick
    /// rather than a slash, why the Temper has no area ability, why Hallow's protections are the
    /// one thing allowed to be held up permanently — is in <c>docs/ABILITIES.md</c>, and the rules
    /// it argues for are enforced by <c>AbilityContentTests</c> against the shipped set.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Entry> All { get; } =
    [
        // A kick rather than a slash: the opener must not assume a blade. A Warden with a mace, a
        // staff, or empty hands was still being told they slashed.
        new(CharacterPath.Warden, 1, "warden.kick", "Kick",
            "A boot to the knee. Nothing elegant, and it does not care what you are holding.",
            CostType.Stamina, 10, 24, null, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.1", "3")))),

        new(CharacterPath.Adept, 1, "adept.bolt", "Bolt",
            "A thrown splinter of raw force.",
            CostType.Focus, 15, 24, 8, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.2", "4")))),

        new(CharacterPath.Temper, 1, "temper.strike", "Quick Strike",
            "In and out before it turns.",
            CostType.Stamina, 12, 24, null, TargetingType.SingleTarget,
            (Effect("damage.physical", Damage("1.25", "4")))),

        // Not Self, and that is the example worth setting: a support Path whose heals only reach
        // itself is not a support Path. Cast with no target named, it still lands on the caster.
        new(CharacterPath.Hallow, 1, "hallow.mend", "Mend",
            "Close what is open, on yourself or on someone beside you.",
            CostType.Focus, 20, 24, 4, TargetingType.SingleTarget,
            (Effect("heal.restore", Heal("25")))),
    ];

    /// <summary>Every ability this Path learns, in unlock order.</summary>
    public static IReadOnlyList<Entry> For(CharacterPath path) =>
        [.. All.Where(e => e.Path == path).OrderBy(e => e.UnlockLevel)];

    /// <summary>Builds the <see cref="Ability"/> row for an entry.</summary>
    /// <summary>
    /// Every (ability, effect) pair in the starter set.
    /// </summary>
    /// <remarks>
    /// An ability carries a list now, so a question about effects - "does every stun sit under its
    /// clamp" - is a question about this rather than about <see cref="All"/>. Iterating abilities
    /// and reaching for a first effect would answer it only for as long as nothing has two.
    /// </remarks>
    public static IEnumerable<(Entry Entry, AbilityEffectSpec Effect)> AllEffects =>
        All.SelectMany(entry => entry.Effects.Select(effect => (entry, effect)));

    /// <summary>
    /// The whole starter set as <see cref="Ability"/> rows — what a fresh database is seeded with.
    /// </summary>
    /// <remarks>
    /// <b>This is the starter set, not the live one.</b> Anything asking what abilities exist
    /// *now* must read the <c>abilities</c> table (through <c>AbilityCache</c> at runtime), because
    /// a builder can add, retune, and remove rows and none of that reaches this list. The
    /// legitimate callers are the seeder, which plants these on first boot, and tests that assert
    /// about the shipped set specifically.
    /// </remarks>
    public static IReadOnlyList<Ability> AsAbilities { get; } = [.. All.Select(ToAbility)];

    public static Ability ToAbility(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new Ability
        {
            Key = entry.Key,
            Path = entry.Path,
            UnlockLevel = entry.UnlockLevel,
            Name = entry.Name,
            Description = entry.Description,
            CostType = entry.CostType,
            CostValue = entry.CostValue,
            CooldownPulses = entry.CooldownPulses,
            CooldownGroup = entry.CooldownGroup,
            CastTimePulses = entry.CastTimePulses,
            TargetingType = entry.TargetingType,
            Effects = [.. entry.Effects.Select(e =>
                new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))],
        };
    }
}

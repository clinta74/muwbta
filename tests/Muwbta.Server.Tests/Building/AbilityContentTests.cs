using System.Globalization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Server.Building;

namespace Muwbta.Server.Tests.Building;

/// <summary>
/// The ability set the game ships, and the properties that keep level-ups meaning something
/// (PLAN.md §4.5, docs/ABILITIES.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>These guards used to run over <c>AbilityCatalogue</c>, and moved here with the abilities
/// themselves.</b> The catalogue is four examples now — enough to seed an empty database, not
/// enough to be a game — and <c>shipped/abilities.json</c> is the set. Assertions about the
/// shipped set belong with the shipped set, which is the same move <see cref="WeaponBalanceTests"/>
/// made for weapons.
/// </para>
/// <para>
/// What each one is for is unchanged and is worth keeping in front of whoever edits the numbers:
/// every one of these was written after the thing it forbids had already happened. Weakens that
/// protected their target, wounds that expired before their first tick, ten of eleven timed
/// effects holdable forever, fourteen cooldowns that never lined up with a swing.
/// </para>
/// </remarks>
public sealed class AbilityContentTests
{
    private static readonly CharacterPath[] Paths =
        [CharacterPath.Warden, CharacterPath.Adept, CharacterPath.Temper, CharacterPath.Hallow];

    /// <summary>The executors that exist, asked of the registry rather than listed again here.</summary>
    private static readonly EffectRegistry KnownEffects = new();

    /// <summary>
    /// Abilities meant to be held up permanently, by key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was <c>Entry.Maintainable</c>, a bool on the catalogue record. It could not travel:
    /// the engine never reads it — no column, no bundle field — so it was always an annotation on
    /// a balance rule rather than game data, and adding it to the schema to carry a test's
    /// exception list would be the tail wagging the dog.
    /// </para>
    /// <para>
    /// A named list is also more honest than a flag buried in a table of sixty-nine. The
    /// exception is Hallow's group protection and only that: a long duration with a short cooldown
    /// is what lets the group be set up <em>before</em> the fight and still be covered at the end
    /// of it, so the Hallow spends the fight healing rather than re-casting. A self-buff kept up
    /// forever is the free power the rule exists to stop.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> Maintainable = new(StringComparer.Ordinal)
    {
        "hallow.fortitude",
        "hallow.aegis",
        "hallow.sanctuary",
        "hallow.consecration",
        "hallow.the-long-vigil",
    };

    /// <summary>
    /// The shipped abilities, as the import would store them.
    /// </summary>
    /// <remarks>
    /// Defaults applied the way <c>WorldImporter.AbilityChangeFor</c> applies them, so what is
    /// asserted here is what would actually land in the table rather than what the file happens
    /// to leave out.
    /// </remarks>
    private static IReadOnlyList<Ability> All
    {
        get
        {
            var path = Path.Combine(RepoPath.Root(), "shipped", "abilities.json");

            Assert.True(File.Exists(path), $"{path} is the ability set and is missing.");
            Assert.True(
                BundleFormat.TryRead(File.ReadAllText(path), out var bundle, out var error), error);

            Assert.NotEmpty(bundle!.Abilities);

            return [.. bundle.Abilities.Select(a => new Ability
            {
                Key = a.Key,
                Path = a.Path ?? CharacterPath.Warden,
                UnlockLevel = a.UnlockLevel,
                Name = a.Name,
                Description = a.Description,
                CostType = a.CostType ?? CostType.Stamina,
                CostValue = a.CostValue,
                CooldownPulses = a.CooldownPulses,
                CooldownGroup = a.CooldownGroup,
                CastTimePulses = a.CastTimePulses,
                TargetingType = a.TargetingType ?? TargetingType.SingleTarget,
                Effects = [.. a.Effects ?? []],
            })];
        }
    }

    private static IEnumerable<Ability> For(CharacterPath path) => All.Where(a => a.Path == path);

    private static IEnumerable<(Ability Entry, AbilityEffectSpec Effect)> AllEffects =>
        All.SelectMany(a => a.Effects.Select(e => (a, e)));

    private static decimal? Read(AbilityEffectSpec effect, string key) =>
        effect.Params.TryGetValue(key, out var raw) &&
        decimal.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // -----------------------------------------------------------------------
    // The set hangs together
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_ability_key_is_unique()
    {
        var duplicates = All
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_ability_the_progression_grants_exists_in_the_set()
    {
        var known = All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var dangling = Paths
            .SelectMany(p => AbilityProgression.GetAbilitiesForPath(All, p))
            .Select(x => x.AbilityKey)
            .Where(key => !known.Contains(key))
            .ToList();

        Assert.Empty(dangling);
    }

    [Fact]
    public void Every_ability_in_the_set_is_reachable_by_some_path()
    {
        // The other direction. `warden.battle-fury`, `adept.weaken`, and `temper.fortify` were once
        // seeded and unreachable - content that existed only in the database.
        var granted = Paths
            .SelectMany(p => AbilityProgression.GetAbilitiesForPath(All, p))
            .Select(x => x.AbilityKey)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = All.Select(e => e.Key).Where(key => !granted.Contains(key)).ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void An_ability_key_is_prefixed_with_the_path_that_learns_it()
    {
        foreach (var entry in All)
        {
            Assert.StartsWith(
                entry.Path.ToString().ToLowerInvariant() + ".", entry.Key, StringComparison.Ordinal);
        }
    }

    // -----------------------------------------------------------------------
    // Effects do what their names say
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_ability_names_an_effect_that_exists()
    {
        // An unknown effect key resolves to nothing and the cast silently does nothing.
        var unknown = AllEffects
            .Where(x => !KnownEffects.Contains(x.Effect.Key))
            .Select(x => $"{x.Entry.Key} -> {x.Effect.Key}")
            .ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// Effects read their parameters by name and skip anything they do not recognise, so a
    /// plausible-but-wrong key produces an ability that costs a resource and does nothing.
    /// </summary>
    [Theory]
    [InlineData("damage.physical", "scalingFactor")]
    [InlineData("buff.damage-up", "outgoingMultiplier")]
    [InlineData("damage.overtime", "tickDamage")]
    [InlineData("damage.overtime", "tickIntervalPulses")]
    public void Every_ability_carries_the_parameter_its_effect_reads(string effectKey, string parameter)
    {
        var missing = AllEffects
            .Where(x => x.Effect.Key == effectKey && !x.Effect.Params.ContainsKey(parameter))
            .Select(x => x.Entry.Key)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Some effects read one of several parameters, and are only broken when they have none.
    /// </summary>
    /// <remarks>
    /// <c>heal.restore</c> takes a flat <c>baseHeal</c> or a proportional <c>healPercent</c>, and
    /// either alone is a working heal. It used to be in the table above, which was right while
    /// there was one way to author a heal and became a test that failed every proportional one the
    /// day there were two. Mirrors <c>AbilityValidator.RequiredOneOf</c>, which is what the builder
    /// enforces on save.
    /// </remarks>
    [Theory]
    [InlineData("heal.restore", new[] { "baseHeal", "healPercent" })]
    [InlineData("resource.restore", new[] { "percent", "amount" })]
    public void Every_ability_carries_one_of_the_parameters_its_effect_reads(
        string effectKey,
        string[] parameters)
    {
        var missing = AllEffects
            .Where(x => x.Effect.Key == effectKey && !parameters.Any(x.Effect.Params.ContainsKey))
            .Select(x => x.Entry.Key)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// A debuff has to move at least one multiplier, and move it the harmful way.
    /// </summary>
    /// <remarks>
    /// This is the assertion that would have caught the original mistake. Every "weaken" was
    /// written as <c>incomingMultiplier</c> below 1.0, which reads plausibly and does the opposite
    /// of what it says: incoming scales the damage the target <em>takes</em>, so those abilities
    /// made their target 25-45% harder to kill. Nothing failed - the spell landed, the effect
    /// appeared on the status screen, and the fight simply went worse.
    /// </remarks>
    [Fact]
    public void Every_debuff_actually_debuffs_its_target()
    {
        foreach (var (entry, effect) in AllEffects.Where(x => x.Effect.Key == "debuff.weaken"))
        {
            var incoming = Read(effect, "incomingMultiplier");
            var outgoing = Read(effect, "outgoingMultiplier");

            Assert.True(
                incoming is not null || outgoing is not null,
                $"{entry.Key} is a debuff that moves neither multiplier.");

            Assert.True(
                incoming is null or > 1.0m,
                $"{entry.Key} sets incomingMultiplier to {incoming}, which protects the target.");

            Assert.True(
                outgoing is null or < 1.0m,
                $"{entry.Key} sets outgoingMultiplier to {outgoing}, which strengthens the target.");
        }
    }

    [Fact]
    public void Every_buff_actually_buffs_its_caster()
    {
        foreach (var (entry, effect) in AllEffects.Where(x => x.Effect.Key == "buff.damage-up"))
        {
            var outgoing = Read(effect, "outgoingMultiplier");

            Assert.True(
                outgoing is > 1.0m,
                $"{entry.Key} sets outgoingMultiplier to {outgoing}, which is not an improvement.");
        }
    }

    /// <summary>
    /// A wound has to actually work: damage above zero, on an interval above zero, for long
    /// enough to tick at least once.
    /// </summary>
    [Fact]
    public void Every_wound_ticks_at_least_once_before_it_expires()
    {
        foreach (var (entry, effect) in AllEffects.Where(x => x.Effect.Key == "damage.overtime"))
        {
            var damage = Read(effect, "tickDamage");
            var interval = Read(effect, "tickIntervalPulses");
            var duration = Read(effect, "durationPulses");

            Assert.True(damage is > 0, $"{entry.Key} ticks for {damage}.");
            Assert.True(interval is > 0, $"{entry.Key} has a tick interval of {interval}.");

            // Strictly greater: a tick due on the expiry pulse is skipped, so a wound whose
            // duration equals its interval ticks zero times.
            Assert.True(
                duration > interval,
                $"{entry.Key} lasts {duration} pulses but ticks every {interval}, so it never lands.");
        }
    }

    /// <summary>
    /// A stun stays short enough to be a tempo tool rather than a removal.
    /// </summary>
    /// <remarks>
    /// <c>StunEffect</c> clamps to its own ceiling, so an over-long value cannot reach play — but
    /// it would clamp <em>silently</em>, and an author who wrote 200 and got 24 would have no
    /// idea. This fails the build instead.
    /// </remarks>
    [Fact]
    public void No_stun_is_authored_longer_than_the_ceiling()
    {
        foreach (var (entry, effect) in AllEffects.Where(x => x.Effect.Key == "control.stun"))
        {
            var duration = Read(effect, "durationPulses");

            Assert.True(duration is > 0, $"{entry.Key} stuns for {duration}.");
            Assert.True(
                duration <= StunEffect.MaxDurationPulses,
                $"{entry.Key} stuns for {duration}, past the {StunEffect.MaxDurationPulses}-pulse ceiling.");
        }
    }

    /// <summary>Snares are clamped for the same reason stuns are, and pinned for the same reason.</summary>
    [Fact]
    public void No_snare_is_authored_longer_than_the_ceiling()
    {
        foreach (var (entry, effect) in AllEffects.Where(x => x.Effect.Key == "control.root"))
        {
            var duration = Read(effect, "durationPulses");

            Assert.True(duration is > 0, $"{entry.Key} snares for {duration}.");
            Assert.True(
                duration <= RootEffect.MaxDurationPulses,
                $"{entry.Key} snares for {duration}, past the {RootEffect.MaxDurationPulses}-pulse ceiling.");
        }
    }

    /// <summary>
    /// Control effects hold one target at a time and never stack, so nothing can be chained into
    /// a permanent lock.
    /// </summary>
    [Fact]
    public void No_control_effect_stacks()
    {
        foreach (var (entry, effect) in AllEffects
            .Where(x => x.Effect.Key.StartsWith("control.", StringComparison.Ordinal)))
        {
            var maxStacks = Read(effect, "maxStacks");

            Assert.True(
                maxStacks is null or 1,
                $"{entry.Key} stacks to {maxStacks}, which chains into a lock.");
        }
    }

    // -----------------------------------------------------------------------
    // Economy and cadence
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_ability_costs_something()
    {
        // A free ability with a cooldown is a worse version of an auto-attack.
        Assert.All(All, e => Assert.True(e.CostValue > 0, $"{e.Key} is free."));
    }

    /// <summary>
    /// Every cooldown is a whole number of two-second combat beats.
    /// </summary>
    /// <remarks>
    /// A swing is 8 pulses (PLAN.md §2.3), so a cooldown that is not a multiple of it never lines
    /// up with the fight: at 20 pulses an opener is ready at 5s, 10s, 15s while swings land at 2s,
    /// 4s, 6s, and the two drift for as long as the fight lasts. Fourteen of the original
    /// thirty-seven were fractional.
    ///
    /// Held on the shipped set rather than in <c>AbilityValidator</c> on purpose. It is a design
    /// rule for this game's abilities, not a law about all possible ones — warning a builder who
    /// types 30 would be nagging about a number that works.
    /// </remarks>
    [Fact]
    public void Every_cooldown_lands_on_the_combat_beat()
    {
        const int PulsesPerSwing = 8;

        var offBeat = All
            .Where(e => e.CooldownPulses % PulsesPerSwing != 0)
            .Select(e => $"{e.Key} at {e.CooldownPulses} pulses ({e.CooldownPulses / 8.0:0.##} beats)")
            .ToList();

        Assert.True(offBeat.Count == 0, "Off the beat:\n  " + string.Join("\n  ", offBeat));
    }

    /// <summary>
    /// Nothing with a duration outlasts its own cooldown.
    /// </summary>
    /// <remarks>
    /// Buffs, debuffs and wounds all refresh rather than stack, so a duration longer than the
    /// cooldown means the effect can be held up permanently and the cooldown does nothing at all.
    /// Ten of the eleven timed effects were in that state — Weaken at 200% uptime, Scorch at 225%.
    ///
    /// Two exceptions, both deliberate. <b>A wound may exactly equal its cooldown</b>: re-applying
    /// a damage-over-time as it falls off <em>is</em> the rotation, and each application costs the
    /// resource and the turn again. And <b>a multi-stack effect</b> is meant to be re-applied
    /// inside its own duration — that is what the stacks are.
    /// </remarks>
    [Fact]
    public void No_timed_effect_can_be_maintained_permanently()
    {
        var permanent = new List<string>();

        foreach (var (entry, effect) in AllEffects)
        {
            if (Read(effect, "durationPulses") is not { } duration || duration <= 0)
            {
                continue;
            }

            if ((Read(effect, "maxStacks") ?? 1) > 1 || Maintainable.Contains(entry.Key))
            {
                continue;
            }

            var overlaps = effect.Key == "damage.overtime"
                ? duration > entry.CooldownPulses
                : duration >= entry.CooldownPulses;

            if (overlaps)
            {
                permanent.Add(
                    $"{entry.Key} ({effect.Key}) lasts {duration} on a " +
                    $"{entry.CooldownPulses} cooldown ({duration / entry.CooldownPulses:P0} uptime)");
            }
        }

        Assert.True(permanent.Count == 0, "Permanently maintainable:\n  " + string.Join("\n  ", permanent));
    }

    /// <summary>Every key in <see cref="Maintainable"/> is an ability that still exists.</summary>
    /// <remarks>
    /// An exception list outlives what it excepts. A key deleted from content would leave a name
    /// here quietly excusing nothing, and the next ability to earn that name would inherit the
    /// exemption without anybody deciding to grant it.
    /// </remarks>
    [Fact]
    public void Nothing_is_excused_that_does_not_exist()
    {
        var keys = All.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Maintainable, key => !keys.Contains(key));
    }

    // -----------------------------------------------------------------------
    // The shared timer
    // -----------------------------------------------------------------------

    /// <summary>Which abilities are on which timer, stated as content rather than as a shape.</summary>
    /// <remarks>
    /// <b>Listed rather than counted, because sharing a timer is a strong statement</b> and the set
    /// should make it only where it is earned. Both timers exist for the same reason and neither is
    /// about the effect being rare: the Warden's four walls all raise maximum health, and its two
    /// room taunts both write threat for the whole room. In each case using two at once buys a
    /// stretch of a fight in a single beat.
    /// </remarks>
    [Theory]
    [InlineData(CharacterPath.Warden, 1, "warden.ground-and-centre", "warden.last-stand", "warden.the-last-wall", "warden.unbreakable")]
    [InlineData(CharacterPath.Warden, 2, "warden.mass-provocation", "warden.thunderclap")]
    [InlineData(CharacterPath.Hallow, 1, "hallow.smite", "hallow.staunch")]
    public void A_timer_holds_exactly_these(CharacterPath path, int group, params string[] expected)
    {
        // Keyed by (Path, number), which is what Ability.CooldownGroup says the identity is: a
        // character only ever knows one Path's abilities, so each Path numbers from 1 on its own.
        // This asserted on the number alone, which was indistinguishable while only the Warden had
        // timers and became wrong the moment a second Path numbered its first one.
        var onTimer = All
            .Where(a => a.Path == path && a.CooldownGroup == group)
            .Select(a => a.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, onTimer);
    }

    /// <summary>And nothing else is on any timer at all.</summary>
    [Fact]
    public void Nothing_else_shares_a_timer()
    {
        Assert.DoesNotContain(All, a => a.CooldownGroup is not null and not (1 or 2));
    }

    /// <summary>
    /// Each ability names its timer-mates, which is what the roster line and a refusal print.
    /// </summary>
    [Fact]
    public void Each_ability_on_a_timer_knows_the_others_on_it()
    {
        var all = All;

        foreach (var shared in all.Where(a => a.CooldownGroup is not null))
        {
            var mates = AbilityCooldowns.GroupMates(shared, all).ToList();

            // A timer of one shares with nothing, which is the silent-does-nothing shape the
            // validator has its own arm for. Here it would also mean the list above is stale.
            Assert.NotEmpty(mates);
            Assert.All(mates, m => Assert.Equal(shared.CooldownGroup, m.CooldownGroup));
        }
    }

    /// <summary>
    /// A timer rations one thing, and every ability on it does that thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule behind the Warden's timers, and the reason a player can predict them: its timer 1
    /// is the abilities that raise maximum health, timer 2 the ones that taunt the whole room. An
    /// ability sharing a timer for some other reason would be a refusal nobody could work out from
    /// what the two abilities do.
    /// </para>
    /// <para>
    /// <b>The Hallow's timer rations a resource rather than an effect, and is exempt.</b> Smite and
    /// Staunch are a strike and a heal — nothing about their effects is shared. What they have in
    /// common is the bar: they are the only two things a Hallow spends stamina on, and the timer is
    /// there to make the second bar a decision between them rather than a second rotation. That is
    /// still predictable, which is what this rule is protecting — a player can see that their two
    /// stamina moves share a clock — but it is predictable from the cost rather than from the
    /// effect, so it cannot be asserted here.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CharacterPath.Warden, 1, "buff.max-health")]
    [InlineData(CharacterPath.Warden, 2, "control.taunt")]
    public void Every_ability_on_a_timer_does_the_thing_that_timer_rations(
        CharacterPath path,
        int group,
        string effectKey)
    {
        var onTimer = All.Where(a => a.Path == path && a.CooldownGroup == group).ToList();

        Assert.NotEmpty(onTimer);
        Assert.All(onTimer, a => Assert.Contains(a.Effects, e => e.Key == effectKey));
    }

    /// <summary>
    /// A timer that rations a resource holds abilities that all spend that resource.
    /// </summary>
    /// <remarks>
    /// The Hallow's half of the rule above. The effects differ on purpose; the cost is the thing
    /// that has to agree, or the timer is arbitrary again.
    /// </remarks>
    [Fact]
    public void The_hallows_timer_rations_its_stamina()
    {
        var onTimer = All
            .Where(a => a.Path == CharacterPath.Hallow && a.CooldownGroup == 1)
            .ToList();

        Assert.NotEmpty(onTimer);
        Assert.All(onTimer, a => Assert.Equal(CostType.Stamina, a.CostType));

        // And they are the whole of what the Path spends stamina on. A third stamina ability off
        // the timer would make the pairing look like a rule when it had become an exception.
        var allStamina = All
            .Where(a => a.Path == CharacterPath.Hallow && a.CostType == CostType.Stamina)
            .Select(a => a.Key)
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(
            onTimer.Select(a => a.Key).OrderBy(k => k, StringComparer.Ordinal),
            allStamina);
    }

    /// <summary>
    /// Two room taunts fired together would buy a lead of 85% of a health bar in one beat.
    /// </summary>
    /// <remarks>
    /// <b>Found in play.</b> Thunderclap and Mass Provocation were both castable at once, and a
    /// taunt does not overwrite the last one — <c>Combat.ForceTopHater</c> sets the caster to the
    /// highest hate <em>plus</em> the lead, so 0.35 of the bar and then 0.5 on top of it. That is
    /// most of a fight's worth of threat spent on a decision that is not a decision, and it is the
    /// pair being on one timer that turns it back into one.
    /// </remarks>
    [Fact]
    public void The_room_taunts_cannot_be_stacked_on_each_other()
    {
        var taunts = All.Where(a => a.CooldownGroup == 2).ToList();

        Assert.All(taunts, a => Assert.Equal(TargetingType.Aoe, a.TargetingType));

        var lead = taunts
            .SelectMany(a => a.Effects.Where(e => e.Key == "control.taunt"))
            .Sum(e => decimal.Parse(e.Params["leadFraction"], CultureInfo.InvariantCulture));

        Assert.True(
            lead > 0.75m,
            $"These share a timer because together they are worth {lead:P0} of a health bar. " +
            "If that is no longer true, the timer is worth re-deciding rather than assuming.");
    }

    /// <summary>No timer has exactly one ability on it, which would be a group of one.</summary>
    [Fact]
    public void No_timer_is_shared_with_nothing()
    {
        Assert.DoesNotContain(
            AbilityValidator.ValidateSet(All, KnownEffects),
            p => p.Message.Contains("shares with nothing", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the shipped set carries no warnings either, not merely no errors.
    /// </summary>
    /// <remarks>
    /// The higher bar, deliberately. The warnings are about progression shape — a Path with
    /// nothing at level 1, a gap where a level-up gives nothing — and the shipped set is the one
    /// thing designed against those rules rather than merely permitted by them.
    /// </remarks>
    [Fact]
    public void The_shipped_set_is_exemplary_and_not_merely_legal()
    {
        var problems = AbilityValidator.ValidateSet(All, KnownEffects)
            .Select(p => $"{p.Key}: {p.Message}")
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // -----------------------------------------------------------------------
    // Area abilities
    // -----------------------------------------------------------------------

    /// <summary>
    /// An area effect never carries a cast-time-free instant burst on a short cooldown.
    /// </summary>
    /// <remarks>
    /// An AoE pays one cost and one cooldown however many things it lands on, so the same numbers
    /// that are fair on a single target are not fair spread across a room. The floor is
    /// deliberately crude — it catches a room-wide nuke authored with single-target economics,
    /// which is the mistake that is easy to make and hard to notice until a Path trivialises every
    /// group of mobs in the game.
    /// </remarks>
    [Fact]
    public void An_area_ability_costs_more_than_a_single_target_one()
    {
        foreach (var entry in All.Where(e => e.TargetingType == TargetingType.Aoe))
        {
            var comparable = For(entry.Path)
                .Where(e => e.TargetingType == TargetingType.SingleTarget)
                .Where(e => e.UnlockLevel <= entry.UnlockLevel)
                .ToList();

            Assert.All(comparable, single => Assert.True(
                entry.CostValue > single.CostValue || entry.CooldownPulses > single.CooldownPulses,
                $"{entry.Key} hits the whole room for no more cost or cooldown than {single.Key} " +
                "spends on one target."));
        }
    }

    /// <summary>Every area ability points somewhere the area filter understands.</summary>
    [Fact]
    public void Every_area_ability_has_an_executor_behind_it() =>
        Assert.All(
            All.Where(e => e.TargetingType == TargetingType.Aoe),
            entry => Assert.All(entry.Effects, e => Assert.NotNull(KnownEffects.Get(e.Key))));

    // -----------------------------------------------------------------------
    // Levelling
    // -----------------------------------------------------------------------

    [Fact]
    public void Every_path_starts_with_something_castable_at_level_one()
    {
        foreach (var path in Paths)
        {
            Assert.Contains(For(path), e => e.UnlockLevel == 1);
        }
    }

    /// <summary>
    /// The playtesting complaint in one assertion: progression used to stop at level 6 for every
    /// Path, so every level after it granted nothing at all.
    /// </summary>
    [Fact]
    public void Every_path_keeps_unlocking_abilities_to_level_twenty()
    {
        foreach (var path in Paths)
        {
            var levels = For(path).Select(e => e.UnlockLevel).ToList();

            Assert.True(levels.Count >= 6, $"{path} has only {levels.Count} abilities.");
            Assert.True(levels.Max() >= 20, $"{path} stops unlocking at {levels.Max()}.");
        }
    }

    [Fact]
    public void No_path_has_two_abilities_at_the_same_level()
    {
        // One reward per level-up reads more clearly than two at once and none for four levels.
        foreach (var path in Paths)
        {
            var levels = For(path).Select(e => e.UnlockLevel).ToList();
            Assert.Equal(levels.Count, levels.Distinct().Count());
        }
    }

    [Fact]
    public void No_path_goes_more_than_four_levels_without_something_new()
    {
        foreach (var path in Paths)
        {
            var levels = For(path).Select(e => e.UnlockLevel).Order().ToList();

            for (var i = 1; i < levels.Count; i++)
            {
                var gap = levels[i] - levels[i - 1];
                Assert.True(gap <= 4, $"{path} has a {gap}-level gap after {levels[i - 1]}.");
            }
        }
    }

    /// <summary>
    /// And the shipped set passes the validator the import runs, which is the check a builder
    /// meets rather than one this file invents.
    /// </summary>
    [Fact]
    public void The_shipped_set_passes_the_import_validator()
    {
        var problems = AbilityValidator.ValidateSet(All, KnownEffects)
            .Where(p => p.Severity == AbilityProblemSeverity.Error)
            .Select(p => $"{p.Key}: {p.Message}")
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}

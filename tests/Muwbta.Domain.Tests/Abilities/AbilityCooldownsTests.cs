using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Abilities;

/// <summary>
/// Abilities that share a timer, and the ones that do not (PLAN.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// Every cooldown used to be its own, so nothing stopped a Warden chaining four maximum-health
/// buffs into 470 seconds of continuous cover — Ground and Centre alone is nearly permanent, at a
/// 100s cooldown against an 80s duration. A shared timer says: using any of these uses all of them.
/// </para>
/// <para>
/// <b>Nothing about the timer is stored</b>, which is most of why this is worth testing at the
/// Domain level. It is derived from the per-ability cooldowns the world already records, so the
/// property that matters is that an ungrouped ability answers exactly as the old inline arithmetic
/// did — the ungrouped path has to be unchanged by construction, not by inspection.
/// </para>
/// </remarks>
public sealed class AbilityCooldownsTests
{
    private static Ability Ability(
        string key,
        long cooldownPulses,
        int? group = null,
        CharacterPath path = CharacterPath.Warden) => new()
        {
            Key = key,
            Path = path,
            UnlockLevel = 1,
            Name = key,
            Description = string.Empty,
            CostType = CostType.Stamina,
            CostValue = 10,
            CooldownPulses = cooldownPulses,
            CooldownGroup = group,
            TargetingType = TargetingType.Self,
            Effects = [new AbilityEffectSpec("heal.restore", new() { ["baseHeal"] = "10" })],
        };

    /// <summary>Last cast pulses by ability key; anything absent has never been cast.</summary>
    private static Func<Ability, long?> Cast(params (string Key, long Pulse)[] casts)
    {
        var byKey = casts.ToDictionary(c => c.Key, c => (long?)c.Pulse, StringComparer.Ordinal);
        return a => byKey.TryGetValue(a.Key, out var pulse) ? pulse : null;
    }

    // -----------------------------------------------------------------------
    // Own cooldown — unchanged behaviour, stated as a property
    // -----------------------------------------------------------------------

    /// <summary>
    /// Never cast is not the same as cast on pulse 0. Returning 0 for "never" once made the whole
    /// spellbook unusable for its own cooldown after every restart.
    /// </summary>
    [Fact]
    public void An_ability_never_cast_is_ready()
    {
        Assert.Equal(0, AbilityCooldowns.OwnRemaining(Ability("a", 40), null, currentPulse: 0));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(20, 20)]
    [InlineData(39, 1)]
    [InlineData(40, 0)]
    [InlineData(400, 0)]
    public void Own_cooldown_counts_down_and_stops_at_zero(long now, long expected)
    {
        Assert.Equal(
            expected,
            AbilityCooldowns.OwnRemaining(Ability("a", 40), lastCastPulse: 0, currentPulse: now));
    }

    /// <summary>
    /// The whole promise of deriving the timer rather than storing it: an ability that shares
    /// nothing is answered by its own cooldown and nothing else.
    /// </summary>
    [Fact]
    public void An_ungrouped_ability_is_blocked_only_by_itself()
    {
        var alone = Ability("warden.kick", 40);
        var other = Ability("warden.bash", 400);

        var blocked = AbilityCooldowns.Blocking(
            alone, [alone, other], Cast(("warden.bash", 0)), currentPulse: 4);

        Assert.Null(blocked);
    }

    // -----------------------------------------------------------------------
    // Who is on a timer
    // -----------------------------------------------------------------------

    [Fact]
    public void An_ability_on_no_timer_has_no_group_mates()
    {
        var alone = Ability("warden.kick", 40);
        var grouped = Ability("warden.bash", 40, group: 1);

        Assert.Empty(AbilityCooldowns.GroupMates(alone, [alone, grouped]));
    }

    [Fact]
    public void An_ability_is_not_its_own_group_mate()
    {
        var one = Ability("warden.one", 40, group: 1);
        var two = Ability("warden.two", 40, group: 1);

        Assert.Equal(["warden.two"], AbilityCooldowns.GroupMates(one, [one, two]).Select(a => a.Key));
    }

    /// <summary>
    /// <b>The number is only half a timer's identity.</b> A character knows one Path's abilities, so
    /// two Paths using 1 are two timers — and numbering each Path from 1 is what an author will do.
    /// </summary>
    [Fact]
    public void A_timer_belongs_to_one_path()
    {
        var warden = Ability("warden.one", 40, group: 1);
        var temper = Ability("temper.one", 40, group: 1, path: CharacterPath.Temper);

        Assert.Empty(AbilityCooldowns.GroupMates(warden, [warden, temper]));
        Assert.Empty(AbilityCooldowns.GroupMates(temper, [warden, temper]));
    }

    [Fact]
    public void Different_numbers_on_one_path_are_different_timers()
    {
        var walls = Ability("warden.walls", 40, group: 1);
        var guards = Ability("warden.guards", 40, group: 2);

        Assert.Empty(AbilityCooldowns.GroupMates(walls, [walls, guards]));
    }

    // -----------------------------------------------------------------------
    // What a timer refuses
    // -----------------------------------------------------------------------

    /// <summary>Using one puts the whole timer down, for that ability's own cooldown.</summary>
    [Fact]
    public void Using_one_blocks_the_others_on_its_timer()
    {
        var used = Ability("warden.unbreakable", 1200, group: 1);
        var other = Ability("warden.ground-and-centre", 400, group: 1);

        var blocked = AbilityCooldowns.Blocking(
            other, [used, other], Cast(("warden.unbreakable", 0)), currentPulse: 8);

        Assert.NotNull(blocked);
        Assert.Equal("warden.unbreakable", blocked!.Value.Source.Key);
        Assert.Equal(1192, blocked.Value.RemainingPulses);
    }

    /// <summary>And the whole timer frees together.</summary>
    [Fact]
    public void The_timer_frees_when_the_ability_that_locked_it_does()
    {
        var used = Ability("warden.unbreakable", 1200, group: 1);
        var other = Ability("warden.ground-and-centre", 400, group: 1);
        var casts = Cast(("warden.unbreakable", 0));

        Assert.NotNull(AbilityCooldowns.Blocking(other, [used, other], casts, currentPulse: 1199));
        Assert.Null(AbilityCooldowns.Blocking(other, [used, other], casts, currentPulse: 1200));
        Assert.Null(AbilityCooldowns.Blocking(used, [used, other], casts, currentPulse: 1200));
    }

    /// <summary>
    /// The shorter ability locks the timer for the shorter time, which is the half that keeps a
    /// timer from being "the longest cooldown, always".
    /// </summary>
    [Fact]
    public void Using_the_shorter_ability_locks_the_longer_one_only_briefly()
    {
        var quick = Ability("warden.ground-and-centre", 400, group: 1);
        var slow = Ability("warden.unbreakable", 1200, group: 1);

        var blocked = AbilityCooldowns.Blocking(
            slow, [quick, slow], Cast(("warden.ground-and-centre", 0)), currentPulse: 0);

        Assert.Equal(400, blocked!.Value.RemainingPulses);
        Assert.Null(AbilityCooldowns.Blocking(
            slow, [quick, slow], Cast(("warden.ground-and-centre", 0)), currentPulse: 400));
    }

    /// <summary>The longest cooldown on the timer wins, whichever ability owns it.</summary>
    [Fact]
    public void The_longest_running_cooldown_on_the_timer_is_the_one_reported()
    {
        var a = Ability("warden.a", 100, group: 1);
        var b = Ability("warden.b", 1200, group: 1);
        var c = Ability("warden.c", 40, group: 1);

        var blocked = AbilityCooldowns.Blocking(
            c, [a, b, c], Cast(("warden.a", 0), ("warden.b", 0)), currentPulse: 10);

        Assert.Equal("warden.b", blocked!.Value.Source.Key);
        Assert.Equal(1190, blocked.Value.RemainingPulses);
    }

    /// <summary>
    /// When the ability itself is cooling for as long as a group-mate, the refusal is about the one
    /// the player actually typed — a message naming somebody else would read as a bug.
    /// </summary>
    [Fact]
    public void An_ability_cooling_on_its_own_account_reports_itself()
    {
        var typed = Ability("warden.a", 400, group: 1);
        var mate = Ability("warden.b", 400, group: 1);

        var blocked = AbilityCooldowns.Blocking(
            typed, [typed, mate], Cast(("warden.a", 0), ("warden.b", 0)), currentPulse: 0);

        Assert.Equal("warden.a", blocked!.Value.Source.Key);
    }

    /// <summary>A Temper's timer never reaches a Warden, whatever number either of them used.</summary>
    [Fact]
    public void A_timer_on_another_path_blocks_nothing()
    {
        var warden = Ability("warden.one", 400, group: 1);
        var temper = Ability("temper.one", 1200, group: 1, path: CharacterPath.Temper);

        Assert.Null(AbilityCooldowns.Blocking(
            warden, [warden, temper], Cast(("temper.one", 0)), currentPulse: 0));
    }

    // The four walls, and everything else about the timer the game actually ships, moved to
    // AbilityContentTests when the ability set moved to shipped/abilities.json. What stays here is
    // the timer's behaviour, which is Domain's and is asked of fixtures.

    // -----------------------------------------------------------------------
    // What the validator says about a timer
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_timer_that_is_not_a_positive_number_is_refused(int group)
    {
        var problems = AbilityValidator.ValidateOne(
            Ability("warden.a", 40, group: group), new EffectRegistry());

        Assert.Contains(problems, p =>
            p.Severity == AbilityProblemSeverity.Error &&
            p.Message.Contains("shared timer", StringComparison.Ordinal));
    }

    /// <summary>
    /// A timer with one ability on it shares with nothing: the field is set, the editor shows it,
    /// and no cast is ever refused because of it. The silent-does-nothing shape the validator is
    /// shaped around.
    /// </summary>
    [Fact]
    public void A_timer_with_one_ability_on_it_is_a_warning()
    {
        var lonely = Ability("warden.a", 40, group: 7);

        var problems = AbilityValidator.ValidateSet([lonely], new EffectRegistry());

        Assert.Contains(problems, p =>
            p.Key == "warden.a" &&
            p.Severity == AbilityProblemSeverity.Warning &&
            p.Message.Contains("timer 7", StringComparison.Ordinal));
    }

    [Fact]
    public void A_timer_with_two_abilities_on_it_is_not_complained_about()
    {
        var one = Ability("warden.a", 40, group: 7);
        var two = Ability("warden.b", 40, group: 7);

        Assert.DoesNotContain(
            AbilityValidator.ValidateSet([one, two], new EffectRegistry()),
            p => p.Message.Contains("timer 7", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two Paths on number 1 are two timers, so both are lonely — and saying so is the point, since
    /// a builder who thought they had grouped them needs telling that they had not.
    /// </summary>
    [Fact]
    public void The_same_number_under_two_paths_leaves_both_sharing_nothing()
    {
        var warden = Ability("warden.a", 40, group: 1);
        var temper = Ability("temper.a", 40, group: 1, path: CharacterPath.Temper);

        var problems = AbilityValidator.ValidateSet([warden, temper], new EffectRegistry())
            .Where(p => p.Message.Contains("timer 1", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, problems.Count);
    }

}

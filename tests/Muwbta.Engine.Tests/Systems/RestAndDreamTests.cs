using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// Posture: what a character sitting or lying down may do, and what reaches them while asleep.
/// </summary>
public sealed class RestAndDreamTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");

    /// <summary>The test world with its west room made peaceful, which is where sleep is allowed.</summary>
    /// <remarks>
    /// Every test below that types <c>sleep</c> starts here, because the verb reads the
    /// <c>peaceful</c> flag (PLAN.md §4.10) and the test world declares nothing. Fights are staged
    /// in <see cref="Middle"/> instead: the room you may sleep in is by definition a room nothing
    /// can open a fight in, so the two cannot be the same room.
    /// </remarks>
    private static WorldHarness Bedroom()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.MakePeaceful(West);
        return harness;
    }

    // -----------------------------------------------------------------------
    // Where you may lie down at all
    // -----------------------------------------------------------------------

    [Fact]
    public void Sleep_is_refused_in_a_room_that_is_not_peaceful()
    {
        // Absence is the safe value (§4.10): a room nobody has thought about is not a bedroom.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");

        Assert.Equal(CharacterRestState.Stand, player.Character.RestState);
        Assert.Contains("not safe to sleep", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_names_rest_as_the_thing_to_do_instead()
    {
        // The shape every other gate uses: say the state, then name the way out of it. "You cannot
        // sleep here" on its own leaves a player standing in a corridor with no move to make.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");

        Assert.Contains("rest", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Resting_is_allowed_where_sleeping_is_not()
    {
        // The point of the gate: recovery is never taken away, only its best rate is. That is what
        // stops `sleep` being the correct thing to type in every room in the world.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "rest");

        Assert.Equal(CharacterRestState.Rest, player.Character.RestState);
    }

    [Fact]
    public void A_peaceful_room_takes_sleepers()
    {
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");

        Assert.Equal(CharacterRestState.Sleep, player.Character.RestState);
    }

    [Fact]
    public void The_flag_one_scope_up_is_enough()
    {
        // Flags resolve nearest-level-wins (§4.10), which is the granularity that matters here: a
        // settlement zone declares itself peaceful once and every inn inside it takes sleepers.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Peaceful.Key, true));
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");

        Assert.Equal(CharacterRestState.Sleep, player.Character.RestState);
    }

    [Fact]
    public void So_is_the_flag_on_the_world()
    {
        // The whole-realm case, for a world that wants sleeping to work anywhere inside it.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Mutate(new SetWorldFlag("test", RoomFlags.Peaceful.Key, true));
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");

        Assert.Equal(CharacterRestState.Sleep, player.Character.RestState);
    }

    [Fact]
    public void A_room_that_stops_being_peaceful_under_a_sleeper_leaves_them_asleep()
    {
        // Nothing wakes them, and asking again says what they are rather than telling them off. A
        // builder unticking a checkbox is not an event to narrate into somebody's sleep, and no
        // other rule in the engine changes a posture on a player's behalf either.
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, false));
        harness.Drain(player);

        harness.Execute(player, "sleep");

        Assert.Equal(CharacterRestState.Sleep, player.Character.RestState);
        Assert.Contains("already asleep", harness.DrainText(player), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Standing up first
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("sleep")]
    [InlineData("rest")]
    public void You_cannot_open_a_fight_from_the_floor(string posture)
    {
        // The defect this suite was written for. Movement refused a resting character and `attack`
        // did not, so a fight could be started and held to the end without ever standing - while
        // drawing the resting regen rate the whole time.
        var harness = Bedroom();

        var player = harness.AddPlayer("Kael", West, level: 10);
        harness.AddMob("rat", West, health: 200);

        harness.Execute(player, posture);
        harness.Drain(player);

        harness.Execute(player, "attack rat");

        Assert.Equal(CombatState.Idle, player.Character.CombatState);
        Assert.Contains("stand", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sleep")]
    [InlineData("rest")]
    public void You_cannot_cast_from_the_floor(string posture)
    {
        var harness = Bedroom();

        var player = harness.AddPlayer("Kael", West, level: 10);
        harness.AddMob("rat", West, health: 200);

        harness.Execute(player, posture);
        harness.Drain(player);

        harness.Execute(player, "cast bolt rat");

        var said = harness.DrainText(player);
        Assert.Contains("stand", said, StringComparison.OrdinalIgnoreCase);

        // And it is refused as a *posture* problem rather than as an unknown ability - the refusal
        // lands before the ability is resolved, so the answer is about the player, not the spell.
        Assert.DoesNotContain("don't know", said, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sleep")]
    [InlineData("rest")]
    public void You_cannot_walk_out_from_the_floor(string posture)
    {
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, posture);
        harness.Drain(player);

        harness.Execute(player, "east");

        Assert.Equal(West, player.Character.RoomKey);
        Assert.Contains("stand", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standing_up_puts_all_three_back()
    {
        var harness = Bedroom();

        var player = harness.AddPlayer("Kael", West, level: 10);

        // Next door, because the room that took the sleeper cannot also hold the fight - and
        // walking there is itself the third of the three a sleeper cannot do.
        harness.AddMob("rat", Middle, health: 200);

        harness.Execute(player, "sleep");
        harness.Execute(player, "stand");
        harness.Drain(player);

        harness.Execute(player, "east");
        harness.Execute(player, "attack rat");

        Assert.Equal(Middle, player.Character.RoomKey);
        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
    }

    [Fact]
    public void The_refusal_names_the_posture_you_are_actually_in()
    {
        // "You must stand up first" is the wrong answer to a player who thinks they are standing.
        // Both states refuse, and each says which one it is.
        var asleep = WorldHarness.NewCharacter("Kael", West);
        asleep.RestState = CharacterRestState.Sleep;
        Assert.Contains("asleep", RestGate.Refuse(asleep)!, StringComparison.Ordinal);

        var sitting = WorldHarness.NewCharacter("Kael", West);
        sitting.RestState = CharacterRestState.Rest;
        Assert.Contains("sitting", RestGate.Refuse(sitting)!, StringComparison.Ordinal);

        var standing = WorldHarness.NewCharacter("Kael", West);
        standing.RestState = CharacterRestState.Stand;
        Assert.Null(RestGate.Refuse(standing));
    }

    // -----------------------------------------------------------------------
    // What reaches a sleeper
    // -----------------------------------------------------------------------

    [Fact]
    public void A_sleeping_player_is_not_shown_another_players_emote()
    {
        var harness = Bedroom();

        var sleeper = harness.AddPlayer("Kael", West);
        var awake = harness.AddPlayer("Ilse", West);

        harness.Execute(sleeper, "sleep");
        harness.Drain(sleeper);
        harness.Drain(awake);

        harness.Execute(awake, "emote waves a lantern about");

        Assert.DoesNotContain("lantern", harness.DrainText(sleeper), StringComparison.Ordinal);
    }

    [Fact]
    public void An_awake_player_in_the_same_room_still_sees_it()
    {
        // The filter is per-player, not per-room. One person dozing must not silence the room for
        // everybody else in it.
        var harness = Bedroom();

        var sleeper = harness.AddPlayer("Kael", West);
        var awake = harness.AddPlayer("Ilse", West);
        var watcher = harness.AddPlayer("Bram", West);

        harness.Execute(sleeper, "sleep");
        harness.Drain(watcher);

        harness.Execute(awake, "emote waves a lantern about");

        Assert.Contains("lantern", harness.DrainText(watcher), StringComparison.Ordinal);
    }

    [Fact]
    public void Speech_still_gets_through_to_a_sleeper()
    {
        // Deliberate. An emote is something you do where people can see it; being shouted at is
        // how somebody wakes you, and filtering it would leave no way to reach a sleeping player.
        var harness = Bedroom();

        var sleeper = harness.AddPlayer("Kael", West);
        var awake = harness.AddPlayer("Ilse", West);

        harness.Execute(sleeper, "sleep");
        harness.Drain(sleeper);

        harness.Execute(awake, "say wake up");

        Assert.Contains("wake up", harness.DrainText(sleeper), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Dreams
    // -----------------------------------------------------------------------

    [Fact]
    public void Falling_asleep_does_not_dream_immediately()
    {
        // Dropping off and dreaming on the same tick reads as a bug rather than as sleep.
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        harness.Drain(player);

        DreamSystem.Tick(harness.World, 0);

        Assert.Equal(string.Empty, harness.DrainText(player));
    }

    [Fact]
    public void A_sleeper_dreams_once_every_five_minutes()
    {
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        harness.Drain(player);

        DreamSystem.Tick(harness.World, 0);
        Assert.Equal(string.Empty, harness.DrainText(player));

        // A minute short of due: still nothing.
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses - 240);
        Assert.Equal(string.Empty, harness.DrainText(player));

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses);
        Assert.Contains("You dream", harness.DrainText(player), StringComparison.Ordinal);

        // And not again until the next interval.
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses + 240);
        Assert.Equal(string.Empty, harness.DrainText(player));

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 2);
        Assert.Contains("You dream", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Somebody_awake_never_dreams()
    {
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        for (var pulse = 0L; pulse <= DreamSystem.IntervalPulses * 3; pulse += 240)
        {
            DreamSystem.Tick(harness.World, pulse);
        }

        Assert.Equal(string.Empty, harness.DrainText(player));
    }

    [Fact]
    public void Waking_up_and_sleeping_again_starts_a_fresh_five_minutes()
    {
        // The timer is cleared on waking. Without that, somebody who slept an hour ago and lies
        // down again dreams on the very next tick from a stale due-time.
        var harness = Bedroom();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        DreamSystem.Tick(harness.World, 0);
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 5);
        harness.Drain(player);

        harness.Execute(player, "stand");
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 5);

        harness.Execute(player, "sleep");
        harness.Drain(player);

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 5);
        Assert.Equal(string.Empty, harness.DrainText(player));

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 6);
        Assert.Contains("You dream", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_sleepers_in_one_room_do_not_dream_the_same_thing()
    {
        // Both sleepers dream. Whether they dream the *same* thing is asserted below against the
        // function itself: with ten lines and two arbitrary ids they collide about one time in
        // ten, so comparing what two random characters were sent is sampling rather than testing.
        // This test failed roughly that often until it stopped claiming otherwise.
        var harness = Bedroom();

        var one = harness.AddPlayer("Kael", West);
        var two = harness.AddPlayer("Ilse", West);

        harness.Execute(one, "sleep");
        harness.Execute(two, "sleep");
        harness.Drain(one);
        harness.Drain(two);

        DreamSystem.Tick(harness.World, 0);
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses);

        Assert.Contains("You dream", harness.DrainText(one), StringComparison.Ordinal);
        Assert.Contains("You dream", harness.DrainText(two), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dream_is_keyed_to_the_character_so_a_room_is_not_a_broadcast()
    {
        // Spread across the table, asserted over fixed ids rather than over two.
        //
        // Two is the wrong number for this. Ten lines means any *particular* pair collides one
        // time in ten - which is what made the original test flaky, and what made my first
        // attempt at replacing it fail outright when the two ids I picked happened to be a
        // colliding pair. The claim worth making is that the id moves the line at all, and that
        // is a claim about the set.
        var lines = Enumerable
            .Range(0, 32)
            .Select(i => new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]))
            .Select(id => DreamSystem.LineFor(id, DreamSystem.IntervalPulses))
            .Distinct()
            .Count();

        // Deliberately weak: true for any sane hash, and false for the failure that matters -
        // every sleeper in the world reading the same sentence.
        Assert.True(lines > 1, $"32 characters produced {lines} distinct dreams");
    }

    [Fact]
    public void One_sleeper_dreams_something_new_each_time()
    {
        // The property that does hold unconditionally: the line advances with the dream count, so
        // an hour asleep is not the same sentence twelve times.
        var kael = new Guid("11111111-1111-1111-1111-111111111111");

        Assert.NotEqual(
            DreamSystem.LineFor(kael, DreamSystem.IntervalPulses),
            DreamSystem.LineFor(kael, DreamSystem.IntervalPulses * 2));
    }

    [Fact]
    public void The_same_sleeper_at_the_same_moment_dreams_the_same_thing()
    {
        // Pure function of (id, pulse). Nothing is stored and no random source is threaded through
        // the loop, which is the whole reason it is derived this way.
        var kael = new Guid("11111111-1111-1111-1111-111111111111");

        Assert.Equal(
            DreamSystem.LineFor(kael, DreamSystem.IntervalPulses),
            DreamSystem.LineFor(kael, DreamSystem.IntervalPulses));
    }

    [Fact]
    public void Somebody_does_dream_of_electric_sheep()
    {
        Assert.Contains(
            DreamSystem.Lines,
            line => line.Contains("electric sheep", StringComparison.Ordinal));
    }
}

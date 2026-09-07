using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// Walking behind somebody without typing the direction (PLAN.md §4.17).
/// </summary>
/// <remarks>
/// Three rules carry the feature, and each is a test here rather than a comment: it is group-only,
/// it follows <em>walking</em> and nothing else, and a step it cannot take ends it rather than
/// being retried. The third is what stops somebody ending up several rooms behind and unaware,
/// which is the state the verb exists to prevent.
/// </remarks>
public sealed class AutoFollowTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static void Group(WorldHarness harness, PlayerActor leader, PlayerActor member)
    {
        harness.Execute(leader, $"group invite {member.Name}");
        harness.Execute(member, "group accept");
    }

    /// <summary>Two grouped players in the west room, the second following the first.</summary>
    private static (WorldHarness Harness, PlayerActor Kael, PlayerActor Ilse) Following()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);
        Group(harness, kael, ilse);

        harness.Execute(ilse, "autofollow Kael");
        harness.Drain(kael);
        harness.Drain(ilse);

        return (harness, kael, ilse);
    }

    // -----------------------------------------------------------------------
    // Walking
    // -----------------------------------------------------------------------

    [Fact]
    public void A_follower_walks_along()
    {
        var (harness, kael, ilse) = Following();

        harness.Execute(kael, "east");

        Assert.Equal(Middle, ilse.RoomKey);
        Assert.Contains("You follow Kael east.", harness.DrainText(ilse), StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_comes_along_in_order()
    {
        // C follows B follows A. All three were in the same room, so the same origin applies to
        // every hop of the propagation.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);
        var bram = harness.AddPlayer("Bram", West);

        Group(harness, kael, ilse);
        Group(harness, kael, bram);

        harness.Execute(ilse, "autofollow Kael");
        harness.Execute(bram, "autofollow Ilse");

        harness.Execute(kael, "east");

        Assert.Equal(Middle, ilse.RoomKey);
        Assert.Equal(Middle, bram.RoomKey);
    }

    [Fact]
    public void Somebody_in_another_room_stays_put_and_keeps_following()
    {
        // Following is a standing intent rather than a leash: being elsewhere is not a failure, and
        // they pick the leader up again the next time they are together for a step.
        var (harness, kael, ilse) = Following();

        harness.Execute(ilse, "east");
        harness.Execute(kael, "east");

        Assert.Equal(Middle, ilse.RoomKey);
        Assert.Equal(ilse.CharacterId, harness.World.FollowersOf(kael.CharacterId).Single().CharacterId);
    }

    // -----------------------------------------------------------------------
    // A step that cannot be taken ends it
    // -----------------------------------------------------------------------

    [Fact]
    public void A_locked_exit_stops_the_follower_and_ends_the_follow()
    {
        // The gate is re-asked of the follower rather than assumed from the leader passing it.
        // Skipping it would be a way to walk anybody through any lock (§4.15).
        var (harness, kael, ilse) = Following();
        harness.World.FindRoom(West)!.ExitTo(Direction.East)!.RequiredItemKey = "brass-key";

        harness.GiveItem(kael, harness.DefineItem("brass-key", "a brass key", slot: null));

        harness.Execute(kael, "east");

        Assert.Equal(Middle, kael.RoomKey);
        Assert.Equal(West, ilse.RoomKey);
        Assert.Contains("You lose sight of Kael.", harness.DrainText(ilse), StringComparison.Ordinal);
        Assert.Empty(harness.World.FollowersOf(kael.CharacterId));
    }

    [Fact]
    public void A_follower_in_a_fight_stops_following()
    {
        var (harness, kael, ilse) = Following();
        ilse.Character.CombatState = CombatState.Fighting;

        harness.Execute(kael, "east");

        Assert.Equal(West, ilse.RoomKey);
        Assert.Empty(harness.World.FollowersOf(kael.CharacterId));
    }

    [Fact]
    public void A_sleeping_follower_stops_following()
    {
        var (harness, kael, ilse) = Following();
        harness.MakePeaceful(West);
        harness.Execute(ilse, "sleep");
        harness.Drain(ilse);

        harness.Execute(kael, "east");

        Assert.Equal(West, ilse.RoomKey);
        Assert.Empty(harness.World.FollowersOf(kael.CharacterId));
    }

    // -----------------------------------------------------------------------
    // Only walking counts
    // -----------------------------------------------------------------------

    [Fact]
    public void A_relocation_that_is_not_a_walk_ends_every_follow()
    {
        // The invariant lives in WorldState.Move rather than at each teleporting verb, so a
        // relocation added later gets it without knowing this feature exists.
        var (harness, kael, ilse) = Following();

        var dropped = harness.World.Move(kael, East);

        Assert.Equal(ilse.CharacterId, Assert.Single(dropped).CharacterId);
        Assert.Empty(harness.World.FollowersOf(kael.CharacterId));
        Assert.Equal(West, ilse.RoomKey);
    }

    [Fact]
    public void Walking_is_the_one_relocation_that_does_not()
    {
        var (harness, kael, ilse) = Following();

        harness.Execute(kael, "east");

        Assert.Equal(ilse.CharacterId, harness.World.FollowersOf(kael.CharacterId).Single().CharacterId);
    }

    // -----------------------------------------------------------------------
    // The verb
    // -----------------------------------------------------------------------

    [Fact]
    public void Following_is_group_only()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);
        harness.Drain(ilse);

        harness.Execute(ilse, "autofollow Kael");

        Assert.Contains("not in your group", harness.DrainText(ilse), StringComparison.Ordinal);
        Assert.Empty(harness.World.FollowersOf(kael.CharacterId));
    }

    [Fact]
    public void Following_somebody_who_follows_you_is_refused()
    {
        var (harness, kael, ilse) = Following();

        harness.Execute(kael, "autofollow Ilse");

        Assert.Contains(
            "Ilse is already following you.",
            harness.DrainText(kael),
            StringComparison.Ordinal);
        Assert.Null(harness.World.LeaderOf(kael.CharacterId));
    }

    [Fact]
    public void A_longer_ring_is_refused_too()
    {
        // A three-person party is enough to build one, and nothing else would catch it.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);
        var bram = harness.AddPlayer("Bram", West);

        Group(harness, kael, ilse);
        Group(harness, kael, bram);

        harness.Execute(ilse, "autofollow Kael");
        harness.Execute(bram, "autofollow Ilse");
        harness.Drain(kael);

        harness.Execute(kael, "autofollow Bram");

        Assert.Contains("round in a circle", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Null(harness.World.LeaderOf(kael.CharacterId));
    }

    [Fact]
    public void Naming_the_same_person_again_turns_it_off()
    {
        var (harness, kael, ilse) = Following();

        harness.Execute(ilse, "autofollow Kael");

        Assert.Contains("You stop following Kael.", harness.DrainText(ilse), StringComparison.Ordinal);

        harness.Execute(kael, "east");
        Assert.Equal(West, ilse.RoomKey);
    }

    [Fact]
    public void Bare_autofollow_stops()
    {
        var (harness, kael, ilse) = Following();

        harness.Execute(ilse, "autofollow");

        Assert.Contains("You stop following.", harness.DrainText(ilse), StringComparison.Ordinal);
        Assert.Empty(harness.World.FollowersOf(kael.CharacterId));
    }

    [Fact]
    public void You_cannot_follow_yourself()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "autofollow Kael");

        Assert.Null(harness.World.LeaderOf(kael.CharacterId));
    }

    [Fact]
    public void The_leader_is_told()
    {
        // Being followed is something you are entitled to know about, group or not.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);
        Group(harness, kael, ilse);
        harness.Drain(kael);

        harness.Execute(ilse, "autofollow Kael");

        Assert.Contains(
            "Ilse begins following you.",
            harness.DrainText(kael),
            StringComparison.Ordinal);
    }
}

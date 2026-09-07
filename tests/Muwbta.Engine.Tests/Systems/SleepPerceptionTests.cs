using Muwbta.Domain.Characters;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// What reaches a sleeping player, and what does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seen is filtered; heard is not.</b> Somebody walking in, walking out, or picking something up
/// is a thing you perceive by looking, and a sleeper is not looking. What is said to the room still
/// arrives, because that is the point of shouting at somebody.
/// </para>
/// <para>
/// This rule already existed in the codebase twice, written by hand — <c>emote</c> and the mob idle
/// lines each skipped sleepers themselves — and everywhere else did not. Sleeping otherwise costs
/// nothing and pays the best regen in the game, so the sleeper was also the best-informed person in
/// the room. <c>WorldState.AwakeIn</c> is now the one list, and these tests are what stop the next
/// announcement being added to the wrong one.
/// </para>
/// </remarks>
public sealed class SleepPerceptionTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>A sleeper, and somebody awake beside them to prove the line was sent at all.</summary>
    private static (WorldHarness Harness, PlayerActor Sleeper, PlayerActor Awake) Bedroom()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        // A bedroom is a peaceful room, since that is the only kind `sleep` accepts (§4.10).
        harness.MakePeaceful(West);

        var sleeper = harness.AddPlayer("Wen", West, path: CharacterPath.Hallow, level: 10);
        var awake = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        harness.Execute(sleeper, "sleep");
        Assert.Equal(CharacterRestState.Sleep, sleeper.Character.RestState);

        harness.Drain(sleeper);
        harness.Drain(awake);

        return (harness, sleeper, awake);
    }

    /// <summary>Somebody standing next door, ready to walk in and back out again.</summary>
    /// <remarks>The test world is west-middle-east, so next door to west is middle.</remarks>
    private static PlayerActor Visitor(WorldHarness harness) =>
        harness.AddPlayer(
            "Bram", RoomKey.Parse("test.zone.middle"), path: CharacterPath.Warden, level: 10);

    // -----------------------------------------------------------------------
    // Seen
    // -----------------------------------------------------------------------

    [Fact]
    public void A_sleeper_is_not_told_that_somebody_walked_in()
    {
        var (harness, sleeper, awake) = Bedroom();
        var visitor = Visitor(harness);

        harness.Execute(visitor, "west");

        Assert.DoesNotContain("Bram", harness.DrainText(sleeper), StringComparison.Ordinal);
        Assert.Contains("Bram arrives", harness.DrainText(awake), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sleeper_is_not_told_that_somebody_walked_out()
    {
        var (harness, sleeper, awake) = Bedroom();
        var visitor = Visitor(harness);

        harness.Execute(visitor, "west");
        harness.Drain(sleeper);
        harness.Drain(awake);

        harness.Execute(visitor, "east");

        Assert.DoesNotContain("Bram", harness.DrainText(sleeper), StringComparison.Ordinal);
        Assert.Contains("Bram leaves", harness.DrainText(awake), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sleeper_is_not_told_that_somebody_picked_something_up()
    {
        var (harness, sleeper, awake) = Bedroom();

        var fang = harness.AddItemTemplate(new ItemTemplate
        {
            Key = "fang",
            Name = "a tarnished fang",
            Description = "On the floor.",
            Icon = "$",
        });
        harness.DropItemInRoom(fang, West);

        harness.Execute(awake, "get fang");

        Assert.DoesNotContain("takes", harness.DrainText(sleeper), StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule this one was already written by hand for, now going through the shared list rather
    /// than its own copy of the check.
    /// </summary>
    [Fact]
    public void A_sleeper_is_not_told_about_an_emote()
    {
        var (harness, sleeper, awake) = Bedroom();

        harness.Execute(awake, "emote waves");

        Assert.Empty(harness.DrainText(sleeper).Trim());
        Assert.Contains("waves", harness.DrainText(awake), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Heard
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>Speech is deliberately not filtered.</b> `say` is addressed at the room, and being
    /// reachable by somebody shouting at you is most of what makes sleeping in company safe.
    /// </summary>
    [Fact]
    public void A_sleeper_is_still_told_what_is_said()
    {
        var (harness, sleeper, awake) = Bedroom();

        harness.Execute(awake, "say wake up");

        Assert.Contains("wake up", harness.DrainText(sleeper), StringComparison.Ordinal);
    }

    /// <summary>
    /// Standing up puts them back in the audience — nothing about the filter is sticky.
    /// </summary>
    [Fact]
    public void Standing_up_puts_them_back_in_the_room()
    {
        var (harness, sleeper, _) = Bedroom();
        var visitor = Visitor(harness);

        harness.Execute(sleeper, "stand");
        harness.Drain(sleeper);

        harness.Execute(visitor, "west");

        Assert.Contains("Bram arrives", harness.DrainText(sleeper), StringComparison.Ordinal);
    }
}

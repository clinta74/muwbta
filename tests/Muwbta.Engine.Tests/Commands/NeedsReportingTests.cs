using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Where a player can find out they are hungry, other than by having been watching when
/// <c>NeedsSystem</c> said so once.
/// </summary>
/// <remarks>
/// <para>
/// Hunger and thirst are announced on the tick that crosses a threshold and never again, and the
/// only other thing they do is slow recovery — a multiplier nothing prints. So the two places that
/// have to answer are the sheet you look yourself up on and the verb you type to recover, and these
/// are the tests that would notice either falling silent.
/// </para>
/// <para>
/// Asserted on the words rather than on the numbers, because the words are the feature. The
/// thresholds behind them are <c>NeedsTests</c>' business.
/// </para>
/// </remarks>
public sealed class NeedsReportingTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static (WorldHarness Harness, PlayerActor Actor) Player(int hunger = 0, int thirst = 0)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        // Peaceful because half these tests type `sleep`, which asks for it (§4.10). Nothing here
        // fights, so there is no reason to be selective about which room gets the flag.
        harness.MakePeaceful(Room);

        var actor = harness.AddPlayer("Kael", Room);
        actor.Character.Vitals.Hunger = hunger;
        actor.Character.Vitals.Thirst = thirst;

        return (harness, actor);
    }

    private static string Say(WorldHarness harness, PlayerActor actor, string input)
    {
        harness.Drain(actor);
        harness.Execute(actor, input);
        return harness.DrainText(actor);
    }

    [Fact]
    public void The_sheet_names_both_needs_when_both_have_been_let_slide()
    {
        var (harness, actor) = Player(hunger: 95, thirst: 45);

        var sheet = Say(harness, actor, "stats");

        Assert.Contains("Condition: starving and thirsty", sheet, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fed is an answer too. Printing the line only when something is wrong would leave a player
    /// who came here to ask unable to tell "fine" from "this screen does not cover that".
    /// </summary>
    [Fact]
    public void The_sheet_says_so_when_there_is_nothing_wrong()
    {
        var (harness, actor) = Player();

        var sheet = Say(harness, actor, "stats");

        Assert.Contains("Condition: fed and watered", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovering at", sheet, StringComparison.Ordinal);
    }

    /// <summary>The cost, in the units it is actually paid in.</summary>
    [Fact]
    public void The_sheet_prices_a_neglected_need_against_the_normal_rate()
    {
        var (harness, actor) = Player(thirst: Needs.Worst);

        var sheet = Say(harness, actor, "stats");

        Assert.Contains(
            $"Recovering at {Needs.SlowestRegenShare:P0} of your normal rate", sheet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rest")]
    [InlineData("sleep")]
    public void Settling_in_to_recover_names_what_is_slowing_it(string verb)
    {
        var (harness, actor) = Player(hunger: 75, thirst: 92);

        var told = Say(harness, actor, verb);

        Assert.Contains("You are very hungry and parched.", told, StringComparison.Ordinal);
        Assert.Contains("recover at", told, StringComparison.Ordinal);
    }

    /// <summary>
    /// Below the first threshold there is nothing worth saying, and saying it anyway on every
    /// <c>rest</c> is how a player learns to read past the line that matters.
    /// </summary>
    [Theory]
    [InlineData("rest")]
    [InlineData("sleep")]
    public void A_fed_character_lies_down_without_a_lecture(string verb)
    {
        var (harness, actor) = Player(hunger: Needs.Thresholds[0] - 1, thirst: Needs.Thresholds[0] - 1);

        var told = Say(harness, actor, verb);

        Assert.DoesNotContain("You are", told, StringComparison.Ordinal);
        Assert.DoesNotContain("recover at", told, StringComparison.Ordinal);
    }

    /// <summary>Standing up is leaving; there is nothing to do about it from up there.</summary>
    [Fact]
    public void Standing_up_does_not_remind()
    {
        var (harness, actor) = Player(hunger: 95, thirst: 95);
        harness.Execute(actor, "rest");

        var told = Say(harness, actor, "stand");

        Assert.DoesNotContain("starving", told, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reminder is for the person lying down. Nobody else in the room asked about their belly.
    /// </summary>
    [Fact]
    public void The_room_is_not_told_who_is_hungry()
    {
        var (harness, actor) = Player(hunger: 95);
        var witness = harness.AddPlayer("Bram", Room);

        harness.Drain(witness);
        harness.Execute(actor, "rest");

        var seen = harness.DrainText(witness);

        Assert.Contains("sits down to rest", seen, StringComparison.Ordinal);
        Assert.DoesNotContain("starving", seen, StringComparison.Ordinal);
    }
}

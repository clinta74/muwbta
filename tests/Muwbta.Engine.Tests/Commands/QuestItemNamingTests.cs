using Muwbta.Domain.Quests;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Quests naming their items to players.
/// </summary>
/// <remarks>
/// Reported from play as <em>"You don't have enough ossara-fallen-marker."</em> — the same defect
/// as the mob one before it, wearing a different disguise. <c>ItemInstance.DisplayName</c> fixed
/// every site that had an instance to ask; a quest requirement names its item by <em>key</em>,
/// because the player might be holding none of them and still has to be told what to go and find.
/// So the sweep that caught the others could not have caught these.
///
/// Three lines, and the one on the error path is the least of them: the progress line is read every
/// time anyone checks a quest.
/// </remarks>
public sealed class QuestItemNamingTests
{
    private const string Marker = "ossara-fallen-marker";
    private const string MarkerName = "a fallen road marker";

    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    /// <summary>A giver in the room, a quest wanting four markers, and a player holding one.</summary>
    private static (WorldHarness Harness, Engine.World.PlayerActor Player) Ready(int carried = 1)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.AddMob("ossara-deacon", Room, name: "Deacon Pell of Ilvaro's house");
        var marker = harness.DefineItem(Marker, MarkerName, slot: null);
        harness.DefineQuest(
            "a1-1-the-road-out",
            giverMobKey: "ossara-deacon",
            requiredItemKey: Marker,
            requiredCount: 4,
            rewardItemKey: Marker,
            rewardItemCount: 2);

        var kael = harness.AddPlayer("Kael", Room);
        for (var i = 0; i < carried; i++)
        {
            harness.GiveItem(kael, marker);
        }

        harness.TakeQuest(kael, "pell", "a1-1-the-road-out");
        harness.Drain(kael);

        return (harness, kael);
    }

    [Fact]
    public void A_short_turn_in_names_the_item_and_the_numbers()
    {
        var (harness, kael) = Ready();

        harness.Execute(kael, "give marker pell");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
        Assert.Contains($"You need {MarkerName} (x4).", text, StringComparison.Ordinal);
        Assert.Contains("You have 1.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_progress_line_names_the_item()
    {
        // The one on the common path. Every quest check printed a key.
        var (harness, kael) = Ready();

        harness.Execute(kael, "quest a1-1-the-road-out");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
        Assert.Contains($"Progress: 1/4 — {MarkerName}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reward_line_names_the_item()
    {
        var (harness, kael) = Ready();

        harness.Execute(kael, "quest a1-1-the-road-out");

        var text = harness.DrainText(kael);
        Assert.Contains($"{MarkerName} (x2)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_of_something_carries_no_count()
    {
        // The (xN) shape is the pack listing's (§4.14), and it earns its parenthesis only when
        // there is a number worth reading. "a fallen road marker (x1)" is noise.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.AddMob("ossara-deacon", Room, name: "Deacon Pell of Ilvaro's house");
        harness.DefineItem(Marker, MarkerName, slot: null);
        harness.DefineQuest(
            "single", giverMobKey: "ossara-deacon", requiredItemKey: Marker,
            rewardItemKey: Marker, rewardItemCount: 1);

        var kael = harness.AddPlayer("Kael", Room);
        harness.TakeQuest(kael, "pell", "single");
        harness.Drain(kael);

        harness.Execute(kael, "quest single");

        Assert.DoesNotContain("(x1)", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_template_still_says_the_key()
    {
        // The fallback, and it has to stay: a content bug that leaves the player with a blank
        // where the objective should be is worse than one that shows them an ugly identifier.
        //
        // Taken up first and orphaned afterwards, because a quest whose item template is missing is
        // dormant and is never offered at all (§7.4) - which is the other half of the same rule.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.AddMob("ossara-deacon", Room, name: "Deacon Pell of Ilvaro's house");
        harness.DefineItem("never-authored", "a thing", slot: null);
        harness.DefineQuest(
            "dangling", giverMobKey: "ossara-deacon", requiredItemKey: "never-authored");

        var kael = harness.AddPlayer("Kael", Room);
        harness.TakeQuest(kael, "pell", "dangling");
        harness.ItemTemplates.Remove("never-authored");
        harness.Drain(kael);

        harness.Execute(kael, "quest dangling");

        Assert.Contains("never-authored", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// Taking a quest on says what to bring, by the name in the pack, whatever the giver called it.
    /// </summary>
    /// <remarks>
    /// Playtesting found offers that never named the item at all - "Bring me eight things" for
    /// "something carried in" - so a player could accept a job and not know what it wanted.
    /// </remarks>
    [Fact]
    public void Taking_a_quest_on_names_what_to_bring()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.AddMob("ossara-deacon", Room, name: "Deacon Pell of Ilvaro's house");
        harness.DefineItem(Marker, MarkerName, slot: null);
        harness.DefineQuest(
            "a1-1-the-road-out", giverMobKey: "ossara-deacon", requiredItemKey: Marker, requiredCount: 4);

        var kael = harness.AddPlayer("Kael", Room);
        harness.TakeQuest(kael, "pell", "a1-1-the-road-out");

        Assert.Contains($"You will need {MarkerName} (x4).", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void The_journal_names_the_item_and_the_count()
    {
        var (harness, kael) = Ready();

        harness.Execute(kael, "quests");

        Assert.Contains($"(1/4 {MarkerName})", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>The quest asks for markers, so "markers" is what a player types.</summary>
    [Fact]
    public void A_plural_hands_the_items_in()
    {
        var (harness, kael) = Ready(carried: 4);

        harness.Execute(kael, "give markers pell");

        Assert.DoesNotContain("You don't have", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Equal(
            QuestStatus.Completed,
            harness.World.GetQuestState(kael.Character.Id, "a1-1-the-road-out")?.Status);
    }

    /// <summary>
    /// Three tokens answer to "token"; the one the listener asked for is the one that goes.
    /// </summary>
    /// <remarks>
    /// The debt token is carried first, so the pack's order alone would hand it over as a gift.
    /// </remarks>
    [Fact]
    public void A_give_to_the_one_waiting_hands_over_what_they_asked_for()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.AddMob("nemhal-heretic", Room, name: "Sister Aveth, who was expelled");
        var debt = harness.DefineItem("grask-debt-token", "a debt token", slot: null);
        var vigil = harness.DefineItem("nemhal-vigil-token", "a vigil token", slot: null);
        harness.DefineQuest("a4-1-the-rota", giverMobKey: "nemhal-heretic", requiredItemKey: "nemhal-vigil-token");

        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, debt);
        harness.GiveItem(kael, vigil);
        harness.TakeQuest(kael, "aveth", "a4-1-the-rota");
        harness.Drain(kael);

        harness.Execute(kael, "give token aveth");

        Assert.Equal(
            QuestStatus.Completed,
            harness.World.GetQuestState(kael.Character.Id, "a4-1-the-rota")?.Status);
        Assert.Contains(harness.World.InventoryOf(kael.CharacterId), i => i.TemplateKey == "grask-debt-token");
    }
}

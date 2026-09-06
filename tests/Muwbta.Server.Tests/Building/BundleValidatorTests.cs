using System.Text.Json;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Spawning;
using Muwbta.Server.Building;

namespace Muwbta.Server.Tests.Building;

/// <summary>
/// Every pre-flight rule, one test each (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// These were a Python script with no tests of its own, run when somebody remembered. Two things
/// changed with the port and the second is the larger: the rules now reference
/// <c>RoomFlags</c>, <c>RoomLayoutService</c>, <c>QuestDialogue</c> and <c>MobBehavior</c> instead
/// of recovering them with regular expressions over the C#; and they run in <c>dotnet test</c>,
/// which is the difference between a check and a check somebody remembers.
/// </para>
/// <para>
/// <b>The port found the script had gone quiet.</b> Hoisting the four dialogue literals into
/// <c>QuestDialogue</c> — a refactor with no behaviour in it — left the script's
/// <c>Dialogue\.TryGetValue\("(\w+)"</c> regex matching nothing, so it returned no known keys and
/// skipped the pass entirely, silently, still exiting 0. That pass is the one that caught 35
/// quests' worth of unreachable prose. A guard recovered by pattern-matching source text can be
/// switched off by tidying the source.
/// </para>
/// </remarks>
public sealed class BundleValidatorTests
{
    // Seven rows of seven floor cells: 49, comfortably over the 40-cell minimum, so a test about
    // something else never trips the terrain rule by accident.
    private static readonly IReadOnlyList<string> Floor =
        ["*******", "*******", "*******", "*******", "*******", "*******", "*******"];

    private static readonly Dictionary<string, string> FloorLegend = new() { ["*"] = "floor" };

    private static JsonElement NoFlags => JsonDocument.Parse("{}").RootElement;

    private static JsonElement Flags(string json) => JsonDocument.Parse(json).RootElement;

    private static BundleRoom Room(string key, string zone, params BundleExit[] exits) =>
        new(key, zone, "A room", "It is a room.", NoFlags, Floor, FloorLegend, null, null, exits);

    /// <summary>
    /// Two rooms joined both ways: the smallest bundle that passes everything, so any finding a
    /// test sees is the one it introduced.
    /// </summary>
    private static WorldBundle Valid() => new(
        BundleFormat.CurrentVersion,
        DateTimeOffset.UtcNow,
        new BundleScope("world", "test"),
        [new BundleWorld("test", "Test", "", 0, NoFlags, new Dictionary<string, decimal>())],
        [new BundleZone("test.zone", "test", "Zone", "", 1, 10, NoFlags, new Dictionary<string, decimal>())],
        [
            Room("test.zone.west", "test.zone", new BundleExit("east", "test.zone.east")),
            Room("test.zone.east", "test.zone", new BundleExit("west", "test.zone.west")),
        ],
        [], [], [], [], [], []);

    private static BundleCheck Check(WorldBundle bundle) => BundleValidator.Validate(bundle);

    // -----------------------------------------------------------------------
    // Crossings between Reaches are up from both sides
    // -----------------------------------------------------------------------

    /// <summary>
    /// A bundle carrying two worlds joined by one crossing, which is the shape of every gate in
    /// the Reaches.
    /// </summary>
    private static WorldBundle TwoWorlds(string outDirection, string backDirection) => new(
        BundleFormat.CurrentVersion,
        DateTimeOffset.UtcNow,
        new BundleScope("all", null),
        [
            new BundleWorld("here", "Here", "", 0, NoFlags, new Dictionary<string, decimal>()),
            new BundleWorld("there", "There", "", 1, NoFlags, new Dictionary<string, decimal>()),
        ],
        [
            new BundleZone("here.zone", "here", "Zone", "", 1, 10, NoFlags, new Dictionary<string, decimal>()),
            new BundleZone("there.zone", "there", "Zone", "", 1, 10, NoFlags, new Dictionary<string, decimal>()),
        ],
        [
            Room("here.zone.gate", "here.zone", new BundleExit(outDirection, "there.zone.gate")),
            Room("there.zone.gate", "there.zone", new BundleExit(backDirection, "here.zone.gate")),
        ],
        [], [], [], [], [], []);

    [Fact]
    public void A_crossing_between_worlds_may_be_up_from_both_sides()
    {
        // The portals are deliberately impossible as geography: you go up to leave one Reach and
        // up again to arrive in the next, so neither is above the other. That is what marks the
        // moment as a crossing rather than a walk - every other exit in the game is a compass
        // direction. Reciprocity used to insist on a `down` coming back, which would have made
        // the whole design unauthorable.
        Assert.Empty(Check(TwoWorlds("up", "up")).Findings);
    }

    [Fact]
    public void A_vertical_pair_inside_one_world_still_owes_a_down()
    {
        // The exemption is scoped to a crossing between worlds on purpose. Inside one world a
        // vertical pair is a stair or a cellar - the Terraces' root cellar is exactly that - and
        // two rooms that both go `up` to each other are a mistake worth hearing about.
        var bundle = Valid() with
        {
            Rooms =
            [
                Room("test.zone.west", "test.zone", new BundleExit("up", "test.zone.east")),
                Room("test.zone.east", "test.zone", new BundleExit("up", "test.zone.west")),
            ],
        };

        AssertWarning(bundle, "one-way exit");
    }

    [Fact]
    public void A_crossing_that_is_up_one_way_and_lateral_back_is_still_wrong()
    {
        // Only a matched pair is exempt. One side `up` and the other `north` is the half-applied
        // edit this rule exists to catch.
        AssertWarning(TwoWorlds("up", "north"), "one-way exit");
    }

    private static void AssertError(WorldBundle bundle, string fragment)
    {
        var check = Check(bundle);

        Assert.True(
            check.Errors.Any(f => f.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            $"expected an error mentioning '{fragment}', got: "
            + string.Join(" | ", check.Findings.Select(f => $"{f.Level}: {f.Message}")));
    }

    private static void AssertWarning(WorldBundle bundle, string fragment)
    {
        var check = Check(bundle);

        Assert.True(
            check.Warnings.Any(f => f.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            $"expected a warning mentioning '{fragment}', got: "
            + string.Join(" | ", check.Findings.Select(f => $"{f.Level}: {f.Message}")));
    }

    /// <summary>The baseline. Without this every assertion below could pass for the wrong reason.</summary>
    [Fact]
    public void A_sound_bundle_reports_nothing_at_all()
    {
        var check = Check(Valid());

        Assert.Empty(check.Findings);
        Assert.True(check.Ok);
    }

    // -----------------------------------------------------------------------
    // Version, worlds and zones
    // -----------------------------------------------------------------------

    [Fact]
    public void A_bundle_of_the_wrong_version_is_refused()
    {
        AssertError(Valid() with { FormatVersion = BundleFormat.CurrentVersion - 1 }, "reads version");
    }

    [Fact]
    public void A_zone_key_must_begin_with_its_world()
    {
        var bundle = Valid();
        var zone = bundle.Zones[0] with { Key = "elsewhere.zone" };

        AssertError(bundle with { Zones = [zone] }, "must begin with its world key");
    }

    [Fact]
    public void A_zone_naming_a_world_the_bundle_does_not_carry_is_a_warning()
    {
        var bundle = Valid();

        AssertWarning(bundle with { Worlds = [] }, "which this bundle does not carry");
    }

    [Fact]
    public void A_zone_with_min_level_above_max_is_an_error()
    {
        var bundle = Valid();
        var zone = bundle.Zones[0] with { MinLevel = 20, MaxLevel = 5 };

        AssertError(bundle with { Zones = [zone] }, "minLevel above maxLevel");
    }

    // -----------------------------------------------------------------------
    // Room keys and zone membership
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("test.zone.Caps")]          // uppercase
    [InlineData("test.zone")]               // two segments
    [InlineData("test.zone.a.b")]           // four segments
    [InlineData("test.zone.-leading")]      // hyphen on the outside
    [InlineData("test.zone.under_score")]
    public void A_room_key_that_is_not_a_RoomKey_is_an_error(string key)
    {
        var bundle = Valid();

        // Replacing the whole room list, so the partner room's exit does not add noise.
        AssertError(bundle with { Rooms = [Room(key, "test.zone")] }, "is not a RoomKey");
    }

    [Fact]
    public void A_room_declaring_a_zone_it_does_not_live_in_is_an_error()
    {
        var bundle = Valid();
        var zones = new List<BundleZone>(bundle.Zones)
        {
            new("test.other", "test", "Other", "", 1, 10, NoFlags, new Dictionary<string, decimal>()),
        };

        var room = bundle.Rooms[0] with { ZoneKey = "test.other" };

        AssertError(bundle with { Zones = zones, Rooms = [room, bundle.Rooms[1]] }, "does not live in it");
    }

    // -----------------------------------------------------------------------
    // Exits
    // -----------------------------------------------------------------------

    [Fact]
    public void Two_exits_in_the_same_direction_is_an_error()
    {
        var bundle = Valid();
        var room = Room(
            "test.zone.west", "test.zone",
            new BundleExit("east", "test.zone.east"),
            new BundleExit("east", "test.zone.east"));

        AssertError(bundle with { Rooms = [room, bundle.Rooms[1]] }, "two east exits");
    }

    [Fact]
    public void An_unknown_direction_is_an_error()
    {
        var bundle = Valid();
        var room = Room("test.zone.west", "test.zone", new BundleExit("sideways", "test.zone.east"));

        AssertError(bundle with { Rooms = [room, bundle.Rooms[1]] }, "unknown direction");
    }

    [Fact]
    public void An_exit_pointing_out_of_the_bundle_is_a_warning()
    {
        var bundle = Valid();
        var room = Room("test.zone.west", "test.zone", new BundleExit("north", "other.zone.hall"));

        AssertWarning(bundle with { Rooms = [room, bundle.Rooms[1]] }, "other.zone.hall");
    }

    /// <summary>
    /// A warning and never an error: a one-way exit can be the story — a mirror you arrive through
    /// and cannot go back out of — and nothing here can tell that from a slip. Erroring would make
    /// the narrative case unauthorable.
    /// </summary>
    [Fact]
    public void A_one_way_exit_is_a_warning_and_does_not_block()
    {
        var bundle = Valid();
        var east = Room("test.zone.east", "test.zone");   // nothing coming back

        var check = Check(bundle with { Rooms = [bundle.Rooms[0], east] });

        Assert.Contains(check.Warnings, f => f.Message.Contains("one-way exit", StringComparison.Ordinal));
        Assert.True(check.Ok, "a one-way exit must not stop an import");
    }

    /// <summary>
    /// The Grask-to-Azhen gate, in miniature: both halves present, directions not opposite. This is
    /// the shape that survived because per-file checking could not see across a realm boundary.
    /// </summary>
    [Fact]
    public void A_return_leg_in_the_wrong_direction_is_still_reported()
    {
        var bundle = Valid();
        var west = Room("test.zone.west", "test.zone", new BundleExit("east", "test.zone.east"));
        var east = Room("test.zone.east", "test.zone", new BundleExit("north", "test.zone.west"));

        var check = Check(bundle with { Rooms = [west, east] });

        Assert.Equal(2, check.Warnings.Count(f => f.Message.Contains("one-way exit", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_room_with_no_path_to_the_rest_is_an_error()
    {
        var bundle = Valid();
        var stranded = Room("test.zone.island", "test.zone");

        AssertError(bundle with { Rooms = [.. bundle.Rooms, stranded] }, "no path to the rest");
    }

    // -----------------------------------------------------------------------
    // Spawners
    // -----------------------------------------------------------------------

    private static BundleSpawner Spawner(
        Guid id, TemplateKind kind, string template, int? fightsAtLevel = null, string? nameModifier = null) =>
        new(id, "test.zone", template, kind, ["test.zone.west"], 1, 60, null, fightsAtLevel, nameModifier);

    [Fact]
    public void Two_spawners_sharing_an_id_is_an_error()
    {
        // Spawner ids are content: re-importing is idempotent only because each carries its own.
        var bundle = Valid();
        var id = Guid.CreateVersion7();

        AssertError(
            bundle with
            {
                MobTemplates = [Mob("rat")],
                Spawners = [Spawner(id, TemplateKind.Mob, "rat"), Spawner(id, TemplateKind.Mob, "rat")],
            },
            "share the id");
    }

    [Fact]
    public void An_item_spawner_with_a_fighting_level_is_an_error()
    {
        var bundle = Valid();

        AssertError(
            bundle with
            {
                ItemTemplates = [Item("torch")],
                Spawners = [Spawner(Guid.CreateVersion7(), TemplateKind.Item, "torch", fightsAtLevel: 4)],
            },
            "fightsAtLevel");
    }

    [Fact]
    public void An_item_spawner_with_a_name_modifier_is_an_error()
    {
        var bundle = Valid();

        AssertError(
            bundle with
            {
                ItemTemplates = [Item("torch")],
                Spawners = [Spawner(Guid.CreateVersion7(), TemplateKind.Item, "torch", nameModifier: "hooded")],
            },
            "nameModifier");
    }

    [Fact]
    public void A_name_modifier_that_starts_with_an_article_is_an_error()
    {
        var bundle = Valid();

        AssertError(
            bundle with
            {
                MobTemplates = [Mob("rat")],
                Spawners = [Spawner(Guid.CreateVersion7(), TemplateKind.Mob, "rat", nameModifier: "a marsh")],
            },
            "article");
    }

    [Fact]
    public void A_name_modifier_on_a_named_character_is_an_error()
    {
        // The one refusal only a bundle can make with certainty: the API may not have the
        // template yet, but a bundle carries both halves.
        var bundle = Valid();

        AssertError(
            bundle with
            {
                MobTemplates = [Mob("tessa", name: "Tessa Roke, armourer")],
                Spawners = [Spawner(Guid.CreateVersion7(), TemplateKind.Mob, "tessa", nameModifier: "marsh")],
            },
            "named character");
    }

    [Fact]
    public void A_name_modifier_on_a_kind_is_fine()
    {
        var bundle = Valid();

        Assert.True(Check(bundle with
        {
            MobTemplates = [Mob("rat")],
            Spawners = [Spawner(Guid.CreateVersion7(), TemplateKind.Mob, "rat", nameModifier: "wharf")],
        }).Ok);
    }

    [Fact]
    public void A_spawner_placing_a_template_the_bundle_does_not_carry_is_a_warning()
    {
        var bundle = Valid();

        AssertWarning(
            bundle with { Spawners = [Spawner(Guid.CreateVersion7(), TemplateKind.Mob, "ghost")] },
            "ghost");
    }

    // -----------------------------------------------------------------------
    // Mobs and quests: the keys the engine reads
    // -----------------------------------------------------------------------

    private static BundleMobTemplate Mob(
        string key, Dictionary<string, object>? behavior = null, string name = "a rat") =>
        new(key, name, "", "r", 1, 16, new Dictionary<string, object>(), 10, 1,
            behavior ?? [], [], []);

    private static BundleItemTemplate Item(string key) =>
        new(key, "a torch", "", "t", null, false, 1, 1, new Dictionary<string, object>(),
            null, null, false, false, false, false, null, null, null);

    /// <summary>
    /// The pass the Python had silently stopped making. Stricter than it, too: its regex swept
    /// every lowercase literal out of <c>MobBehavior.cs</c>, so it accepted <c>aggressive</c> —
    /// a <em>value</em> of the type key — as a bag key of its own.
    /// </summary>
    [Theory]
    [InlineData("aggressive")]
    [InlineData("minSeconds")]
    [InlineData("sentinel")]
    public void A_behavior_key_the_engine_does_not_read_is_an_error(string key)
    {
        var bundle = Valid();

        AssertError(
            bundle with { MobTemplates = [Mob("rat", new Dictionary<string, object> { [key] = true })] },
            $"behavior key '{key}'");
    }

    [Fact]
    public void Every_behavior_key_the_engine_reads_is_accepted()
    {
        var bundle = Valid();
        var behavior = Muwbta.Engine.Inhabitants.MobBehavior.KnownKeys
            .ToDictionary(key => key, object (_) => "x");

        Assert.True(Check(bundle with { MobTemplates = [Mob("rat", behavior)] }).Ok);
    }

    /// <summary>
    /// The defect this whole file exists because of: all 35 quests authored four keys the engine
    /// never read, so ~137 lines of prose were replaced by generic templates with every test green.
    /// </summary>
    [Theory]
    [InlineData("offer")]
    [InlineData("progress")]
    [InlineData("complete")]
    [InlineData("already")]
    public void The_retired_dialogue_keys_are_errors(string key)
    {
        var bundle = Valid();
        var quest = Quest("q1", new Dictionary<string, string> { [key] = "text" });

        AssertError(bundle with { Quests = [quest] }, $"dialogue key '{key}'");
    }

    [Fact]
    public void Every_dialogue_key_the_engine_reads_is_accepted()
    {
        var bundle = Valid();
        // A marked offer, because an unmarked one is now its own error and this test is about
        // which dialogue keys are accepted rather than about what the offer says.
        var dialogue = Muwbta.Domain.Quests.QuestDialogue.All.ToDictionary(
            key => key,
            key => key == "giverOffer" ? "Bring me those <things>." : "text");

        Assert.True(Check(bundle with { Quests = [Quest("q1", dialogue)] }).Ok);
    }

    // -----------------------------------------------------------------------
    // Offer markers
    // -----------------------------------------------------------------------

    /// <summary>
    /// A marker the parser cannot read shows its brackets to a player, so it stops the import.
    /// </summary>
    /// <remarks>
    /// The engine falls open on these — the sentence still reads, with the punctuation in it —
    /// which is the right thing to do at runtime and the wrong thing to ship. This is the only
    /// layer positioned to see it before a player does.
    /// </remarks>
    [Theory]
    [InlineData("Bring me those <things.", "never closed")]
    [InlineData("Bring me those things>.", "never opened")]
    [InlineData("Bring me those <>.", "marks no words")]
    [InlineData("Bring me <those <things>>.", "second marker")]
    public void A_broken_offer_marker_is_an_error(string offer, string complaint)
    {
        var bundle = Valid();
        var quest = Quest("q1", new Dictionary<string, string> { ["giverOffer"] = offer });

        AssertError(bundle with { Quests = [quest] }, complaint);
    }

    /// <summary>
    /// A configuration for none of the worlds in the bundle arrived by accident.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error, because a bundle naming a world it does not define is
    /// legitimate - writing a configuration before importing the world it points into is the normal
    /// order. What is not legitimate is a realm's file carrying a starter configuration for a world
    /// nothing in the file has heard of, which is what a scoped export used to produce.
    /// </remarks>
    [Fact]
    public void A_configuration_for_no_world_here_is_a_warning()
    {
        var bundle = Valid();
        var stranger = new BundleGameConfiguration(
            "elsewhere", "Elsewhere", "", "other.zone.room", "Welcome.", null, ["other"]);

        AssertWarning(bundle with { Configurations = [stranger] }, "carries none of those worlds");
    }

    [Fact]
    public void A_marked_offer_is_accepted()
    {
        var bundle = Valid();
        var quest = Quest(
            "q1", new Dictionary<string, string> { ["giverOffer"] = "Bring me those <things>." });

        Assert.True(Check(bundle with { Quests = [quest] }).Ok);
    }

    /// <summary>
    /// An offer with no marker at all, which the engine tolerates and a player barely finds.
    /// </summary>
    /// <remarks>
    /// Marking is optional to the engine - an unmarked offer falls back to a dim line naming the
    /// command - which is exactly why it is checked here. This and the two below were a test in
    /// this repository that read the shipped world off disk, making them assertions about one
    /// authored world rather than about the format. They belong to every bundle, including one
    /// from a repository this engine has never seen.
    /// </remarks>
    [Fact]
    public void An_offer_that_marks_nothing_cannot_be_clicked()
    {
        var bundle = Valid();
        var quest = Quest(
            "q1", new Dictionary<string, string> { ["giverOffer"] = "Bring me those things." });

        AssertError(bundle with { Quests = [quest] }, "marks nothing in its offer");
    }

    /// <summary>One link per offer. Two would both work, and read as two errands in one sentence.</summary>
    [Fact]
    public void An_offer_marks_one_thing_and_not_two()
    {
        var bundle = Valid();
        var quest = Quest(
            "q1",
            new Dictionary<string, string> { ["giverOffer"] = "He wants <a thing> and <another>." });

        AssertError(bundle with { Quests = [quest] }, "one is the link");
    }

    /// <summary>The marker sits on the noun the errand is about, not around the whole line.</summary>
    [Fact]
    public void A_marker_wrapping_the_line_is_a_parenthetical_with_extra_steps()
    {
        var bundle = Valid();
        var quest = Quest(
            "q1",
            new Dictionary<string, string>
            {
                ["giverOffer"] = "<He would like you to go and fetch the thing for him>.",
            });

        AssertError(bundle with { Quests = [quest] }, "which is most of the line");
    }

    /// <summary>
    /// Two quests one giver could put on the table together must not answer to the same words.
    /// </summary>
    /// <remarks>
    /// The engine refuses to render either link in this case, which is safe and silent. Here is
    /// where the author is told why the link they wrote did not appear.
    /// </remarks>
    [Fact]
    public void Two_quests_offered_together_may_not_mark_the_same_words()
    {
        var bundle = Valid();

        AssertError(
            bundle with { Quests = [Marked("q1", "pell"), Marked("q2", "pell")] },
            "both mark 'things'");
    }

    /// <summary>Different givers, so the command names a different person and cannot collide.</summary>
    [Fact]
    public void The_same_words_are_fine_from_two_different_givers()
    {
        var bundle = Valid();

        Assert.True(Check(bundle with { Quests = [Marked("q1", "pell"), Marked("q2", "vess")] }).Ok);
    }

    /// <summary>
    /// A chain step cannot coincide with the step it follows, however far back that is.
    /// </summary>
    /// <remarks>
    /// Without the transitive walk this rule would be useless on the content it was written for:
    /// Vesh hands out twenty quests and can only ever offer one, and every pair of the twenty
    /// would be reported.
    /// </remarks>
    [Fact]
    public void A_later_step_of_a_chain_may_reuse_the_words()
    {
        var bundle = Valid();

        var first = Marked("q1", "vesh");
        var second = Marked("q2", "vesh") with { PrerequisiteQuestKeys = ["q1"] };
        var third = Marked("q3", "vesh") with { PrerequisiteQuestKeys = ["q2"] };

        Assert.True(Check(bundle with { Quests = [first, second, third] }).Ok);
    }

    /// <summary>Nor can two quests written for Paths that no character holds at once.</summary>
    [Fact]
    public void Quests_for_different_paths_may_reuse_the_words()
    {
        var bundle = Valid();

        var warden = Marked("q1", "vesh") with { Paths = [CharacterPath.Warden] };
        var temper = Marked("q2", "vesh") with { Paths = [CharacterPath.Temper] };

        Assert.True(Check(bundle with { Quests = [warden, temper] }).Ok);
    }

    /// <summary>An empty Path list means anyone, so it overlaps everything.</summary>
    [Fact]
    public void A_quest_for_anyone_still_collides_with_one_for_a_path()
    {
        var bundle = Valid();

        var anyone = Marked("q1", "vesh");
        var temper = Marked("q2", "vesh") with { Paths = [CharacterPath.Temper] };

        AssertError(bundle with { Quests = [anyone, temper] }, "both mark 'things'");
    }

    /// <summary>A quest marking the same words as itself is one quest, and fine.</summary>
    // -----------------------------------------------------------------------
    // Topics (PLAN.md §4.9)
    // -----------------------------------------------------------------------

    private static Dictionary<string, object> Topic(
        string keyword, string text, string? flag = null, string? quest = null)
    {
        var row = new Dictionary<string, object> { ["keyword"] = keyword, ["text"] = text };
        if (flag is not null)
        {
            row["requiresFlag"] = flag;
        }

        if (quest is not null)
        {
            row["requiresQuest"] = quest;
        }
        return row;
    }

    private static BundleMobTemplate WithTopics(string key, params Dictionary<string, object>[] topics) =>
        Mob(key, new Dictionary<string, object>
        {
            ["type"] = "npc",
            ["topics"] = topics.Cast<object>().ToList(),
        });

    [Fact]
    public void A_topic_on_a_mob_is_fine()
    {
        var bundle = Valid();

        Assert.True(Check(bundle with
        {
            MobTemplates = [.. bundle.MobTemplates, WithTopics("adda", Topic("stone", "'Not me.'"))],
        }).Ok);
    }

    /// <summary>
    /// The one that matters: talk tries a giver's quests first, so a topic keyed with a word one
    /// of their offers marks is a topic nobody can ever reach.
    /// </summary>
    [Fact]
    public void A_topic_shadowed_by_the_givers_own_quest_is_an_error()
    {
        var bundle = Valid();

        AssertError(
            bundle with
            {
                MobTemplates = [.. bundle.MobTemplates, WithTopics("elder", Topic("things", "'What things?'"))],
                Quests = [Marked("errand", "elder")],
            },
            "shadowed");
    }

    [Fact]
    public void A_topic_with_no_text_is_an_error()
    {
        var bundle = Valid();
        var half = new Dictionary<string, object> { ["keyword"] = "stone" };

        AssertError(
            bundle with { MobTemplates = [.. bundle.MobTemplates, WithTopics("adda", half)] },
            "no text");
    }

    [Fact]
    public void Two_topics_with_one_keyword_is_an_error()
    {
        var bundle = Valid();

        AssertError(
            bundle with
            {
                MobTemplates = [.. bundle.MobTemplates, WithTopics("adda", Topic("stone", "'One.'"), Topic("Stone", "'Two.'"))],
            },
            "two topics");
    }

    [Fact]
    public void A_topic_gated_on_a_flag_nothing_grants_is_a_warning()
    {
        var bundle = Valid();

        AssertWarning(
            bundle with
            {
                MobTemplates = [.. bundle.MobTemplates, WithTopics("adda", Topic("grask", "'Later.'", flag: "attuned.grask"))],
            },
            "no quest in this bundle grants");
    }

    [Fact]
    public void A_topic_gated_on_a_quest_the_bundle_lacks_is_a_warning()
    {
        var bundle = Valid();

        AssertWarning(
            bundle with
            {
                MobTemplates = [.. bundle.MobTemplates, WithTopics("adda", Topic("ember", "'Later.'", quest: "e1-adept"))],
            },
            "does not carry");
    }

    [Fact]
    public void A_greeting_with_a_malformed_marker_is_an_error()
    {
        var bundle = Valid();
        var greeting = new Dictionary<string, object> { ["greeting"] = new List<object> { "'Mind the <step.'" } };

        AssertError(
            bundle with { MobTemplates = [.. bundle.MobTemplates, Mob("adda", greeting)] },
            "greeting");
    }

    private static BundleQuest Marked(string key, string giver) =>
        Quest(key, new Dictionary<string, string> { ["giverOffer"] = "Bring me those <things>." })
            with { GiverMobKey = giver };

    private static BundleQuest Quest(string key, Dictionary<string, string> dialogue) =>
        new(key, "test.zone", "A quest", "Do the thing", "At length.",
            GiverMobKey: string.Empty, TurninMobKey: string.Empty,
            RequiredItemKey: null, RequiredCount: 1,
            RewardXp: 10, RewardGold: 1, RewardItemKey: null, RewardItemCount: 0, RewardFlagKey: null,
            PrerequisiteQuestKeys: [], IsRepeatable: false, AutoStart: false, Paths: [],
            Dialogue: dialogue, SortOrder: 0);

    // -----------------------------------------------------------------------
    // Terrain
    // -----------------------------------------------------------------------

    [Fact]
    public void A_ragged_grid_is_an_error()
    {
        var bundle = Valid();
        var room = bundle.Rooms[0] with { Grid = ["*******", "****"] };

        AssertError(bundle with { Rooms = [room, bundle.Rooms[1]] }, "rows of differing length");
    }

    [Fact]
    public void A_glyph_with_nothing_in_the_legend_is_an_error()
    {
        var bundle = Valid();
        var room = bundle.Rooms[0] with { Grid = ["*******", "***?***", "*******", "*******", "*******", "*******", "*******"] };

        AssertError(bundle with { Rooms = [room, bundle.Rooms[1]] }, "nothing in its legend");
    }

    [Fact]
    public void A_legend_entry_never_drawn_is_a_warning()
    {
        var bundle = Valid();
        var room = bundle.Rooms[0] with
        {
            Legend = new Dictionary<string, string> { ["*"] = "floor", ["~"] = "water" },
        };

        AssertWarning(bundle with { Rooms = [room, bundle.Rooms[1]] }, "never draws it");
    }

    /// <summary>
    /// A room with nowhere to stand renders with its occupants missing entirely — nothing throws,
    /// the mobs are simply not drawn.
    /// </summary>
    [Fact]
    public void A_room_with_too_little_open_ground_is_an_error()
    {
        var bundle = Valid();
        var room = bundle.Rooms[0] with { Legend = new Dictionary<string, string> { ["*"] = "wall" } };

        AssertError(bundle with { Rooms = [room, bundle.Rooms[1]] }, "cells to stand on");
    }

    // -----------------------------------------------------------------------
    // Flags
    // -----------------------------------------------------------------------

    [Fact]
    public void A_flag_outside_the_registry_is_an_error()
    {
        var bundle = Valid();
        var room = bundle.Rooms[0] with { Flags = Flags("{\"sparkly\": true}") };

        AssertError(bundle with { Rooms = [room, bundle.Rooms[1]] }, "RoomFlags registry");
    }

    [Fact]
    public void A_registered_flag_is_accepted_at_every_level()
    {
        var bundle = Valid();
        var world = bundle.Worlds[0] with { Flags = Flags("{\"pvp\": false}") };
        var zone = bundle.Zones[0] with { Flags = Flags("{\"peaceful\": true}") };
        var room = bundle.Rooms[0] with { Flags = Flags("{\"dark\": true}") };

        Assert.True(Check(bundle with { Worlds = [world], Zones = [zone], Rooms = [room, bundle.Rooms[1]] }).Ok);
    }

    // -----------------------------------------------------------------------
    // The world as authored
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every rule above, run against the real thing — which is the only version of this that can
    /// fail for a reason nobody thought of when writing the rule.
    /// </summary>
    /// <remarks>
    /// <b>Errors only.</b> Each file is one realm, and a realm's gate names a room in the next one,
    /// so a per-file run legitimately warns about references it cannot resolve. Those resolve when
    /// the six are merged; asserting no warnings here would be asserting that the Reaches are six
    /// unconnected worlds.
    /// </remarks>
    [Theory]
    [MemberData(nameof(BundleFormatTests.ContentFiles), MemberType = typeof(BundleFormatTests))]
    public void The_authored_content_has_no_errors_in_it(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Muwbta.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        Assert.True(
            BundleFormat.TryRead(
                File.ReadAllText(Path.Combine(root!.FullName, relativePath)),
                out var bundle,
                out var failure),
            failure);

        var check = BundleValidator.Validate(bundle!);

        Assert.True(
            check.Ok,
            $"{relativePath}:\n  " + string.Join("\n  ", check.Errors.Select(f => f.Message)));
    }
}

using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.Quests;
using Muwbta.Server.Assist;

namespace Muwbta.Server.Building;

/// <summary>How much a finding matters. Only <see cref="Error"/> stops anything.</summary>
public enum BundleFindingLevel
{
    /// <summary>Worth reading. Several are things content is allowed to be.</summary>
    Warning,

    /// <summary>No reading of the content under which this works.</summary>
    Error,
}

public sealed record BundleFinding(BundleFindingLevel Level, string Message);

/// <summary>What a pre-flight check found.</summary>
public sealed record BundleCheck(IReadOnlyList<BundleFinding> Findings)
{
    public IEnumerable<BundleFinding> Errors =>
        Findings.Where(f => f.Level == BundleFindingLevel.Error);

    public IEnumerable<BundleFinding> Warnings =>
        Findings.Where(f => f.Level == BundleFindingLevel.Warning);

    public bool Ok => !Errors.Any();
}

/// <summary>
/// Checks a <see cref="WorldBundle"/> before anyone tries to import it (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>A pre-flight check, not a replacement for <c>?dryRun=true</c>.</b> The dry run is
/// authoritative: it knows what is already in the target database and it is the same code path a
/// real import takes. This needs neither a server nor a database, which is what makes it useful in
/// an editor loop and in the test suite.
/// </para>
/// <para>
/// Three of these are checks the dry run deliberately does not make, because they are authoring
/// mistakes rather than import failures:
/// </para>
/// <list type="bullet">
///   <item><b>Reciprocity.</b> An import applies each edge as given and never invents the return,
///   so a bundle that only says <c>north</c> produces a one-way corridor which imports perfectly
///   and reads as a bug the first time somebody walks it. A <em>warning</em>, and it has to stay
///   one: a one-way can be the story — a mirror you arrive through and cannot go back out of — and
///   nothing here can tell that from a slip.</item>
///   <item><b>Connectivity.</b> A room with no path to the rest of the bundle imports fine and is
///   reachable only by <c>goto</c>.</item>
///   <item><b>A room inside its own zone.</b> <c>ossara.gatetown.x</c> declaring
///   <c>zoneKey: ossara.brackenfell</c> is legal to the engine and is almost always a paste.</item>
/// </list>
/// <para>
/// <b>This was a Python script, and porting it turned four regexes into references.</b> It used to
/// recover the flag registry, the non-placeable tiles, the dialogue keys and the mob behavior keys
/// by running regular expressions over the C# — a second transcription of each, and so a second
/// thing to get wrong. It now reads <see cref="RoomFlags"/>, <see cref="RoomLayoutService"/>,
/// <see cref="QuestDialogue"/> and <see cref="MobBehavior"/> outright, and gets
/// <see cref="RoomKey.TryParse"/> and <see cref="DirectionExtensions.TryParse"/> for free rather
/// than reimplementing the key grammar and the compass.
/// </para>
/// <para>
/// The behavior-key pass is <b>stricter</b> than the script it replaces, and that is the port
/// earning itself: the old regex swept every lowercase literal out of <c>MobBehavior.cs</c>, so it
/// accepted <c>aggressive</c>, <c>npc</c> and <c>passive</c> — <em>values</em> of the type key — as
/// though they were bag keys of their own.
/// </para>
/// </remarks>
public static class BundleValidator
{
    /// <summary>
    /// The fewest open cells a room may leave.
    /// </summary>
    /// <remarks>
    /// Entities are placed only on open ground and are simply not drawn when there is none, so a
    /// room that is all water is a room whose occupants vanish. This is the validator's own rule
    /// rather than a copy of an engine constant — nothing at runtime enforces a floor.
    /// </remarks>
    public const int MinOpenCells = 40;

    public static BundleCheck Validate(WorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var findings = new List<BundleFinding>();

        void Error(string message) => findings.Add(new BundleFinding(BundleFindingLevel.Error, message));
        void Warn(string message) => findings.Add(new BundleFinding(BundleFindingLevel.Warning, message));

        if (!BundleFormat.IsCurrent(bundle))
        {
            Error(BundleFormat.VersionRefusal(bundle.FormatVersion));
        }

        var worlds = bundle.Worlds.Select(w => w.Key).ToHashSet(StringComparer.Ordinal);
        var zones = bundle.Zones.Select(z => z.Key).ToHashSet(StringComparer.Ordinal);
        var items = bundle.ItemTemplates.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var mobs = bundle.MobTemplates.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var rooms = bundle.Rooms.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        CheckZones(bundle, worlds, Error, Warn);
        CheckRooms(bundle, zones, Error, Warn);

        var edges = CollectExits(bundle, rooms, items, Error, Warn);

        CheckReciprocity(edges, rooms, Warn);
        CheckConnectivity(edges, rooms, Error);
        CheckSpawners(bundle, zones, rooms, mobs, items, Error, Warn);
        CheckItems(bundle, Error);
        CheckMobs(bundle, items, Error, Warn);
        CheckQuests(bundle, mobs, items, Error, Warn);
        CheckAbilities(bundle, Error, Warn);
        CheckTerrain(bundle, Error, Warn);
        CheckConfigurations(bundle, Warn);
        CheckFlags(bundle, Error);

        return new BundleCheck(findings);
    }

    private static void CheckZones(
        WorldBundle bundle,
        HashSet<string> worlds,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var zone in bundle.Zones)
        {
            if (!zone.Key.StartsWith(zone.WorldKey + ".", StringComparison.Ordinal))
            {
                error($"zone {zone.Key} must begin with its world key '{zone.WorldKey}' plus a dot");
            }

            if (!worlds.Contains(zone.WorldKey))
            {
                warn($"zone {zone.Key} names world {zone.WorldKey}, which this bundle does not carry");
            }

            if (zone.MinLevel > zone.MaxLevel)
            {
                error($"zone {zone.Key} has minLevel above maxLevel");
            }
        }
    }

    private static void CheckRooms(
        WorldBundle bundle,
        HashSet<string> zones,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var room in bundle.Rooms)
        {
            // One call in place of a length limit, a segment count and a segment grammar written
            // out again in another language. This is the rule the engine itself parses by.
            if (!RoomKey.TryParse(room.Key, out _))
            {
                error(
                    $"room key {room.Key} is not a RoomKey: exactly three dot-separated segments of "
                    + $"lowercase letters, digits and inner hyphens, at most {RoomKey.MaxLength} characters");
            }

            if (!zones.Contains(room.ZoneKey))
            {
                warn($"room {room.Key} names zone {room.ZoneKey}, which this bundle does not carry");
            }
            else if (!room.Key.StartsWith(room.ZoneKey + ".", StringComparison.Ordinal))
            {
                error($"room {room.Key} declares zone {room.ZoneKey} but does not live in it");
            }
        }
    }

    private readonly record struct Edge(string From, Direction Direction, string To);

    private static HashSet<Edge> CollectExits(
        WorldBundle bundle,
        HashSet<string> rooms,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        var edges = new HashSet<Edge>();

        foreach (var room in bundle.Rooms)
        {
            var directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var exit in room.Exits)
            {
                if (!directions.Add(exit.Direction))
                {
                    error($"room {room.Key} has two {exit.Direction} exits");
                }

                // The importer's own parser, so a direction this accepts is one the import takes.
                if (!DirectionExtensions.TryParse(exit.Direction, out var direction))
                {
                    error($"room {room.Key} has an unknown direction '{exit.Direction}'");
                    continue;
                }

                if (!rooms.Contains(exit.To))
                {
                    warn($"room {room.Key} exit {exit.Direction} points at {exit.To}, "
                        + "which this bundle does not carry");
                }

                if (!string.IsNullOrEmpty(exit.RequiredItemKey) && !items.Contains(exit.RequiredItemKey))
                {
                    warn($"room {room.Key} exit {exit.Direction} requires item {exit.RequiredItemKey}, "
                        + "which this bundle does not carry");
                }

                edges.Add(new Edge(room.Key, direction, exit.To));
            }
        }

        return edges;
    }

    /// <summary>
    /// The world a room key belongs to - the first of its three dot-separated segments.
    /// </summary>
    /// <remarks>
    /// Compare these with <see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
    /// and never with <c>==</c> or <c>!=</c>. Span equality compares what the span points at, not
    /// what it contains, so two spans reading "test" out of two different strings are unequal -
    /// which quietly exempted every vertical pair in the world from the reciprocity check until a
    /// test asked for the failing case.
    /// </remarks>
    private static ReadOnlySpan<char> World(string roomKey)
    {
        var dot = roomKey.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? roomKey.AsSpan() : roomKey.AsSpan(0, dot);
    }

    private static void CheckReciprocity(HashSet<Edge> edges, HashSet<string> rooms, Action<string> warn)
    {
        foreach (var edge in edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.Direction))
        {
            if (!rooms.Contains(edge.To))
            {
                // Already warned about as a dangling target; saying it twice helps nobody.
                continue;
            }

            if (edges.Contains(new Edge(edge.To, edge.Direction.Opposite(), edge.From)))
            {
                continue;
            }

            // A crossing between two Reaches is `up` from BOTH sides, which is deliberate and is
            // the whole point of it: up answered by up is impossible as geography, and that is
            // what tells a player they have stopped walking and started transiting. Every other
            // exit in the game is a compass direction, so the one that is not reads as a portal
            // without a word of explanation.
            //
            // Scoped to a crossing between worlds rather than allowed everywhere. Inside one
            // world a vertical pair is a stair or a cellar and still owes a `down` - the
            // Terraces' root cellar is exactly that, and it should keep being checked.
            if (edge.Direction is Direction.Up or Direction.Down
                && !World(edge.From).SequenceEqual(World(edge.To))
                && edges.Contains(new Edge(edge.To, edge.Direction, edge.From)))
            {
                continue;
            }

            warn($"one-way exit: {edge.From} --{edge.Direction.ToLowerName()}--> {edge.To} "
                + $"has no {edge.Direction.Opposite().ToLowerName()} coming back");
        }
    }

    /// <summary>
    /// Anything in its own island is <c>goto</c>-only. Undirected, because a room you can leave but
    /// never reach is just as stranded as one you can reach but never leave.
    /// </summary>
    private static void CheckConnectivity(HashSet<Edge> edges, HashSet<string> rooms, Action<string> error)
    {
        if (rooms.Count == 0)
        {
            return;
        }

        var neighbours = rooms.ToDictionary(
            key => key,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var edge in edges.Where(e => rooms.Contains(e.From) && rooms.Contains(e.To)))
        {
            neighbours[edge.From].Add(edge.To);
            neighbours[edge.To].Add(edge.From);
        }

        var start = rooms.OrderBy(k => k, StringComparer.Ordinal).First();
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var stack = new Stack<string>([start]);

        while (stack.Count > 0)
        {
            foreach (var neighbour in neighbours[stack.Pop()])
            {
                if (seen.Add(neighbour))
                {
                    stack.Push(neighbour);
                }
            }
        }

        foreach (var orphan in rooms.Except(seen).OrderBy(k => k, StringComparer.Ordinal))
        {
            error($"room {orphan} has no path to the rest of the bundle");
        }
    }

    /// <summary>
    /// A configuration's canon against the assist's window (PLAN.md §4.16). A warning, because an
    /// over-long canon imports and runs; it is only that the model stops reading it part-way,
    /// and nothing at runtime says so except the warm-up log.
    /// </summary>
    private static void CheckConfigurations(WorldBundle bundle, Action<string> warn)
    {
        foreach (var configuration in bundle.Configurations)
        {
            var tokens = Canon.EstimateTokens(Canon.Resolve(configuration.Canon));

            if (tokens > AssistOptions.DefaultCanonTokenBudget)
            {
                warn($"configuration {configuration.Key} carries a canon of ~{tokens:N0} tokens, over the "
                    + $"{AssistOptions.DefaultCanonTokenBudget:N0} the assist's window budgets; the model will not read all of it");
            }
        }
    }

    private static void CheckSpawners(
        WorldBundle bundle,
        HashSet<string> zones,
        HashSet<string> rooms,
        HashSet<string> mobs,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        var seen = new HashSet<Guid>();

        // Names as well as keys, because a modifier is judged against the name it goes into. First
        // one wins on a duplicate key; the duplicate is somebody else's finding.
        var mobNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mob in bundle.MobTemplates)
        {
            mobNames.TryAdd(mob.Key, mob.Name);
        }

        foreach (var spawner in bundle.Spawners)
        {
            // Spawner ids are content: re-minting one doubles a zone's population, and two
            // spawners sharing one means the second silently replaces the first on import.
            if (!seen.Add(spawner.Id))
            {
                error($"two spawners share the id {spawner.Id}; re-importing would double the population");
            }

            if (!zones.Contains(spawner.ZoneKey))
            {
                warn($"spawner {spawner.Id} names zone {spawner.ZoneKey}, which this bundle does not carry");
            }

            var known = spawner.TemplateKind == TemplateKind.Mob ? mobs : items;

            if (!known.Contains(spawner.TemplateKey))
            {
                warn($"spawner {spawner.Id} places {spawner.TemplateKey}, which this bundle does not carry");
            }

            if (spawner.TemplateKind == TemplateKind.Item && spawner.FightsAtLevel is not null)
            {
                error($"spawner {spawner.Id} is an item spawner with fightsAtLevel set");
            }

            // The same three refusals the builder API makes (PLAN.md §4.8), repeated here because
            // an import is the one path that does not go through the API - and the third can only
            // be judged with the template in hand, which a bundle has and a PATCH may not.
            if (spawner.NameModifier is { } modifier)
            {
                if (spawner.TemplateKind == TemplateKind.Item)
                {
                    error($"spawner {spawner.Id} is an item spawner with nameModifier set");
                }
                else if (MobNaming.Problem(modifier) is { } problem)
                {
                    error($"spawner {spawner.Id} has nameModifier '{modifier}', which {problem}");
                }
                else if (mobNames.TryGetValue(spawner.TemplateKey, out var name) && !MobNaming.CanModify(name))
                {
                    error($"spawner {spawner.Id} puts nameModifier '{modifier}' on {spawner.TemplateKey}, "
                        + $"whose name '{name}' is a named character's and cannot take one");
                }
            }

            foreach (var roomKey in spawner.RoomKeys.Where(key => !rooms.Contains(key)))
            {
                warn($"spawner {spawner.Id} places into room {roomKey}, which this bundle does not carry");
            }
        }
    }

    /// <summary>
    /// Every <c>baseStats</c> key on an item is one <see cref="EquipmentResolver"/> reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third arm of the content-key guard, and the last one built. <c>baseStats</c> is a free
    /// bag that survives the importer, the writer and the applier untouched, so nothing in the round
    /// trip is in a position to notice a key nobody reads — the same shape as the quest dialogue that
    /// silenced 35 quests, and it has already happened to this bag twice: the retired
    /// <c>armorFlat</c>/<c>armorPercent</c>/<c>armorMultiplier</c> trio, and three vital multipliers
    /// no version ever read.
    /// </para>
    /// <para>
    /// An <b>error</b>, not a warning: there is no reading of an unread stat key under which the
    /// content works. A builder typed a number, it was stored, exported and re-imported, and nothing
    /// will ever consult it.
    /// </para>
    /// </remarks>
    private static void CheckItems(WorldBundle bundle, Action<string> error)
    {
        foreach (var item in bundle.ItemTemplates)
        {
            foreach (var key in (item.BaseStats ?? [])
                .Keys
                .Where(k => !EquipmentResolver.KnownStatKeys.Contains(k))
                .Order(StringComparer.Ordinal))
            {
                error($"{item.Key} has baseStats key '{key}', which the engine does not read; "
                    + $"it reads {string.Join(", ", EquipmentResolver.KnownStatKeys.Order(StringComparer.Ordinal))}");
            }

            // The same rule the builder enforces on save, asked of an authored file - a two-handed
            // shield is not something the API would have accepted, so a bundle carrying one was
            // written by hand or by a generator and nothing else would ever say so.
            if (SlotRules.Incoherent(SlotRules.Normalize(item.Slots), item.IsTwoHanded) is { } why)
            {
                error($"{item.Key}: {why}");
            }

            // A speed on something that reaches neither hand. The delay is the only thing that
            // makes an item a weapon, so this is a weapon that can never swing - and it reads as
            // configured from every screen, which is the failure this whole file exists to catch.
            if (item.AttackDelayPulses is not null && SlotRules.HandSlotsIn(item.Slots).Count == 0)
            {
                error($"{item.Key} declares an attack speed but reaches neither hand, so it can "
                    + "never swing");
            }
        }
    }

    private static void CheckMobs(
        WorldBundle bundle,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        var questKeys = bundle.Quests.Select(q => q.Key).ToHashSet(StringComparer.Ordinal);
        var grantedFlags = bundle.Quests
            .Where(q => !string.IsNullOrWhiteSpace(q.RewardFlagKey))
            .Select(q => q.RewardFlagKey!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var mob in bundle.MobTemplates)
        {
            var behavior = mob.Behavior ?? [];

            foreach (var stocked in MobBehavior.SellsOf(behavior).Where(key => !items.Contains(key)))
            {
                warn($"{mob.Key} sells {stocked}, which this bundle does not carry");
            }

            if (MobBehavior.IsShopkeeper(behavior) && MobBehavior.SellsOf(behavior).Count == 0)
            {
                warn($"{mob.Key} is flagged shopkeeper but stocks nothing");
            }

            foreach (var key in behavior.Keys.Where(k => !MobBehavior.KnownKeys.Contains(k)).Order(StringComparer.Ordinal))
            {
                error($"{mob.Key} has behavior key '{key}', which the engine does not read; "
                    + $"it reads {string.Join(", ", MobBehavior.KnownKeys.Order(StringComparer.Ordinal))}");
            }

            // Greetings may mark topic words now, so a malformed marker is spoken brackets and
            // all - the same finding the quest dialogue check makes, for the same reason.
            foreach (var line in MobBehavior.GreetingsOf(behavior))
            {
                if (QuestOffer.Malformed(line) is { } why)
                {
                    error($"{mob.Key} greeting: {why}");
                }
            }

            CheckTopics(bundle, mob, behavior, questKeys, grantedFlags, error, warn);
        }
    }

    /// <summary>
    /// What a mob can be asked about (PLAN.md §4.9): every row whole, no word given twice, no word
    /// an errand of the same giver already answers to, and every gate naming something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The collision is the one that matters.</b> <c>talk</c> tries a giver's quests before
    /// their topics, so a topic keyed with a word one of their offers marks - or with the last
    /// word of a quest's name, which <c>NameMatch</c> also accepts - is a topic nobody can ever
    /// reach, and nothing at runtime would say so. Checked with the engine's own matcher rather
    /// than a copy of its rules, so the two cannot drift.
    /// </para>
    /// <para>
    /// Gates are warnings, not errors, because a zone-scoped bundle legitimately names a flag
    /// granted three realms up the chain. A row missing its keyword or text is an error, because
    /// the engine drops it silently and a builder would otherwise learn that from a player.
    /// </para>
    /// </remarks>
    private static void CheckTopics(
        WorldBundle bundle,
        BundleMobTemplate mob,
        Dictionary<string, object> behavior,
        HashSet<string> questKeys,
        HashSet<string> grantedFlags,
        Action<string> error,
        Action<string> warn)
    {
        var rows = JsonBag.Items(behavior, MobBehavior.TopicsKey);

        foreach (var entry in rows)
        {
            // A warning rather than an error, to keep the rule that any known key with any value
            // is accepted (a scalar here is dropped by the engine exactly as a scalar emote is
            // not); an object with a half missing is the one shape a builder meant and got wrong.
            if (JsonBag.AsBag(entry) is not { } row)
            {
                warn($"{mob.Key} has a topic that is not an object, which the engine ignores; each needs a keyword and a text");
                continue;
            }

            if (string.IsNullOrWhiteSpace(JsonBag.Text(row, MobBehavior.TopicKeywordKey)))
            {
                error($"{mob.Key} has a topic with no keyword, which nothing can ask for");
            }

            if (string.IsNullOrWhiteSpace(JsonBag.Text(row, MobBehavior.TopicTextKey)))
            {
                error($"{mob.Key} has a topic with no text, which would be answered with silence");
            }
        }

        var topics = MobBehavior.TopicsOf(behavior);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var given = bundle.Quests.Where(q => string.Equals(q.GiverMobKey, mob.Key, StringComparison.Ordinal)).ToList();

        foreach (var topic in topics)
        {
            if (!seen.Add(topic.Keyword))
            {
                error($"{mob.Key} has two topics keyed '{topic.Keyword}'; only the first would ever answer");
            }

            foreach (var quest in given)
            {
                var offer = quest.Dialogue?.GetValueOrDefault(QuestDialogue.GiverOffer);
                var marked = QuestOffer.Keywords(offer)
                    .Any(word => word.Equals(topic.Keyword, StringComparison.OrdinalIgnoreCase));

                if (marked || NameMatch.Matches(topic.Keyword, quest.Name, quest.Key))
                {
                    error($"{mob.Key} topic '{topic.Keyword}' is shadowed by quest {quest.Key}, "
                        + "which answers to the same word first; nobody could ever ask it");
                }
            }

            if (QuestOffer.Malformed(topic.Text) is { } why)
            {
                error($"{mob.Key} topic '{topic.Keyword}': {why}");
            }

            if (topic.RequiresFlag is { } flag && !grantedFlags.Contains(flag))
            {
                warn($"{mob.Key} topic '{topic.Keyword}' requires flag {flag}, which no quest in this bundle grants");
            }

            if (topic.RequiresQuest is { } key && !questKeys.Contains(key))
            {
                warn($"{mob.Key} topic '{topic.Keyword}' requires quest {key}, which this bundle does not carry");
            }
        }
    }

    /// <summary>
    /// Abilities, through the same <see cref="AbilityValidator"/> the builder API saves against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Abilities travelled in bundles for a while with nothing checking them.</b> The import
    /// upserts whatever the file says, so a key naming the wrong Path, a cost of zero, an effect
    /// key no executor implements — all of it landed in <c>abilities</c> and failed silently at
    /// cast time, which is the furthest possible point from the person who wrote it. This is the
    /// same arm as the dialogue-key and behavior-key checks, and it exists for the same reason.
    /// </para>
    /// <para>
    /// <b>The set-level rules only run on a complete set.</b> <see cref="AbilityValidator"/> asks
    /// things a single row cannot answer — whether a Path has anything at level 1, whether the
    /// gaps between unlocks are playable — and those are properties of the whole table. A bundle
    /// carrying one retuned ability on its own would fail every one of them while being perfectly
    /// correct, so they are asked only when the bundle carries all four Paths, which is what a
    /// bundle that means to *be* the ability set looks like.
    /// </para>
    /// <para>
    /// Defaults are applied exactly as <c>WorldImporter.AbilityChangeFor</c> applies them, because
    /// what this validates has to be what the import would store — a null <c>costType</c> becomes
    /// Stamina in both places or the check is inspecting something that never existed.
    /// </para>
    /// </remarks>
    private static void CheckAbilities(WorldBundle bundle, Action<string> error, Action<string> warn)
    {
        if (bundle.Abilities.Count == 0)
        {
            return;
        }

        var effects = new EffectRegistry();
        var abilities = bundle.Abilities.Select(Materialise).ToList();

        var complete = Enum.GetValues<CharacterPath>()
            .All(path => abilities.Any(a => a.Path == path));

        var problems = complete
            ? AbilityValidator.ValidateSet(abilities, effects)
            : [.. abilities.SelectMany(a => AbilityValidator.ValidateOne(a, effects))];

        foreach (var problem in problems)
        {
            var say = problem.Severity == AbilityProblemSeverity.Error ? error : warn;
            say($"ability {problem.Key}: {problem.Message}");
        }
    }

    /// <summary>A bundle ability as the import would store it, defaults and all.</summary>
    private static Ability Materialise(BundleAbility a) => new()
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
    };

    private static void CheckQuests(
        WorldBundle bundle,
        HashSet<string> mobs,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var quest in bundle.Quests)
        {
            foreach (var (value, known, label) in new[]
            {
                (quest.GiverMobKey, mobs, "giver"),
                (quest.TurninMobKey, mobs, "turn-in"),
                (quest.RequiredItemKey, items, "required item"),
                (quest.RewardItemKey, items, "reward item"),
            })
            {
                if (!string.IsNullOrEmpty(value) && !known.Contains(value))
                {
                    warn($"quest {quest.Key} names {label} {value}, which this bundle does not carry");
                }
            }

            // An error rather than a warning: there is no reading of an unread dialogue key under
            // which the content works. The line is authored, stored, exported, re-imported, and
            // never spoken - which is exactly what happened to all 35 quests (BUGS.md #6).
            foreach (var key in (quest.Dialogue ?? [])
                .Keys
                .Where(k => !QuestDialogue.All.Contains(k))
                .Order(StringComparer.Ordinal))
            {
                error($"quest {quest.Key} has dialogue key '{key}', which the engine does not read; "
                    + $"it reads {string.Join(", ", QuestDialogue.All.Order(StringComparer.Ordinal))}");
            }

            // A marker the parser cannot read leaves its brackets showing in the room. Falling
            // open is the right runtime behaviour and the wrong thing to ship, so it is an error
            // here - this is the only layer positioned to see it before a player does.
            if (Offer(quest) is { } offer && QuestOffer.Malformed(offer) is { } complaint)
            {
                error($"quest {quest.Key} has a broken offer marker: {complaint}");
            }
        }

        CheckOfferKeywords(bundle, error);
    }

    /// <summary>
    /// Two quests one giver could offer at the same time must not answer to the same words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The marked words in an offer are what a click sends back (<see cref="QuestOffer"/>), and
    /// the engine resolves them against everything that giver can start right now. Two quests
    /// marking the same phrase means the click lands on whichever the engine reaches first — so
    /// the engine declines to render the link at all and falls back to naming the quest, and this
    /// is where the author finds out why.
    /// </para>
    /// <para>
    /// <b>"Same giver" alone would be useless here.</b> Vesh hands out twenty quests, all of them
    /// either later steps of a chain or written for a different Path, and she can only ever put
    /// one of them on the table — every pair of the twenty would be reported. So a pair is only
    /// checked when it could genuinely coincide: neither reachable from the other through
    /// prerequisites, and their Paths overlapping. Empty Paths means anyone, so it overlaps
    /// everything.
    /// </para>
    /// </remarks>
    private static void CheckOfferKeywords(WorldBundle bundle, Action<string> error)
    {
        var reaches = Reachability(bundle.Quests);

        foreach (var group in bundle.Quests
            .Where(q => !string.IsNullOrEmpty(q.GiverMobKey))
            .GroupBy(q => q.GiverMobKey, StringComparer.Ordinal))
        {
            var quests = group.ToList();

            for (var i = 0; i < quests.Count; i++)
            {
                for (var j = i + 1; j < quests.Count; j++)
                {
                    var (first, second) = (quests[i], quests[j]);

                    if (reaches[(first.Key, second.Key)] || reaches[(second.Key, first.Key)]
                        || Disjoint(first.Paths, second.Paths))
                    {
                        continue;
                    }

                    var shared = QuestOffer.Keywords(Offer(first))
                        .Intersect(QuestOffer.Keywords(Offer(second)), StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (shared.Count > 0)
                    {
                        error($"quests {first.Key} and {second.Key} share giver {group.Key} and can "
                            + $"be offered together, but both mark {string.Join(", ", shared.Select(w => $"'{w}'"))}");
                    }
                }
            }
        }
    }

    /// <summary>Which quests are reachable from which, following prerequisites transitively.</summary>
    private static Dictionary<(string, string), bool> Reachability(IReadOnlyList<BundleQuest> quests)
    {
        var reaches = new Dictionary<(string, string), bool>();

        // Small n (35 today, and this runs once per import), so the plain transitive closure is
        // cheaper to read than anything cleverer.
        foreach (var from in quests)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(from.PrerequisiteQuestKeys ?? []);

            while (pending.Count > 0)
            {
                var key = pending.Pop();

                if (!seen.Add(key))
                {
                    continue;
                }

                foreach (var next in quests.FirstOrDefault(q => q.Key == key)?.PrerequisiteQuestKeys ?? [])
                {
                    pending.Push(next);
                }
            }

            foreach (var to in quests)
            {
                reaches[(from.Key, to.Key)] = seen.Contains(to.Key);
            }
        }

        return reaches;
    }

    /// <summary>Whether these two Path lists can never describe the same character.</summary>
    private static bool Disjoint(List<CharacterPath>? left, List<CharacterPath>? right) =>
        left is { Count: > 0 } && right is { Count: > 0 } && !left.Intersect(right).Any();

    /// <summary>The offer line, or null when the quest leaves it to the engine.</summary>
    private static string? Offer(BundleQuest quest) =>
        quest.Dialogue is not null
            && quest.Dialogue.TryGetValue(QuestDialogue.GiverOffer, out var offer)
                ? offer
                : null;

    /// <summary>
    /// All of these are silent at runtime: an unlisted character draws a tile the legend cannot
    /// explain, a ragged grid renders as a ragged room, and a room with nowhere to stand renders
    /// with its occupants missing entirely.
    /// </summary>
    private static void CheckTerrain(WorldBundle bundle, Action<string> error, Action<string> warn)
    {
        foreach (var room in bundle.Rooms)
        {
            var grid = room.Grid ?? [];
            var legend = room.Legend ?? new Dictionary<string, string>();

            if (grid.Count == 0)
            {
                if (legend.Count > 0)
                {
                    warn($"room {room.Key} has a legend and no grid");
                }

                continue;
            }

            var width = grid[0].Length;

            if (grid.Any(row => row.Length != width))
            {
                error($"room {room.Key} has rows of differing length");
                continue;
            }

            var used = grid.SelectMany(row => row).Select(ch => ch.ToString()).ToHashSet(StringComparer.Ordinal);

            foreach (var glyph in used.Except(legend.Keys).Order(StringComparer.Ordinal))
            {
                error($"room {room.Key} draws '{glyph}' with nothing in its legend");
            }

            foreach (var glyph in legend.Keys.Except(used).Order(StringComparer.Ordinal))
            {
                warn($"room {room.Key} legends '{glyph}' and never draws it");
            }

            var open = grid
                .SelectMany(row => row)
                .Count(ch => legend.TryGetValue(ch.ToString(), out var tile)
                    && !RoomLayoutService.NonPlaceable.Contains(tile));

            if (open < MinOpenCells)
            {
                error($"room {room.Key} leaves {open} cells to stand on, under the {MinOpenCells} minimum");
            }
        }
    }

    private static void CheckFlags(WorldBundle bundle, Action<string> error)
    {
        var known = RoomFlags.All.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var (flags, label, key) in bundle.Worlds
            .Select(w => (w.Flags, "world", w.Key))
            .Concat(bundle.Zones.Select(z => (z.Flags, "zone", z.Key)))
            .Concat(bundle.Rooms.Select(r => (r.Flags, "room", r.Key))))
        {
            if (flags.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            foreach (var flag in flags.EnumerateObject().Where(f => !known.Contains(f.Name)))
            {
                error($"{label} {key} sets '{flag.Name}', which is not in the RoomFlags registry");
            }
        }
    }
}

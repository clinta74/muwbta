using Muwbta.Engine.Inhabitants;
using Muwbta.Domain.Abilities;
using System.Text.Json;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Persistence;
using Muwbta.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Building;

/// <summary>
/// Reads the authored world out of Postgres as a <see cref="WorldBundle"/> (PLAN.md §6, Phase 6).
/// </summary>
/// <remarks>
/// <para>
/// Reads come from the database rather than the loop's world, for the same reason
/// <see cref="BuilderQueries"/> does: enumerating <c>WorldState</c> from a request thread is the
/// race the single-writer rule exists to prevent (§2.1).
/// </para>
/// <para>
/// The interesting part is what a <em>scoped</em> export contains. Rooms, spawners, and quests
/// belong to a zone, so scoping those is a filter. Templates do not - a mob template is global -
/// so a zone export that carried no templates would produce a bundle that imports cleanly and
/// spawns nothing. Instead the scope is closed over references: every template the zone's
/// spawners place, every mob and item its quests name, and every item those mobs drop. The
/// result is a bundle that stands up on its own in an empty database, which is the only kind
/// worth moving between environments.
/// </para>
/// <para>
/// The closure deliberately stops at items. Mobs reference items through their loot tables and
/// items reference nothing, so one pass reaches a fixed point - there is no cycle here to
/// iterate against, and pretending otherwise would be a loop that always runs exactly twice.
/// </para>
/// </remarks>
public sealed class WorldExporter(MuwbtaDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Everything, or one world, or one zone. Returns null when the named world or zone does not
    /// exist - an empty bundle would be indistinguishable from a correct export of empty content.
    /// </summary>
    public async Task<WorldBundle?> ExportAsync(
        string? worldKey,
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(worldKey, zoneKey, cancellationToken);

        if (scope is null)
        {
            return null;
        }

        var (kind, key, zoneKeys) = scope.Value;

        return await BuildAsync(kind, key, zoneKeys, [], cancellationToken);
    }

    /// <summary>The scope kind for a whole configuration: its worlds, and itself.</summary>
    public const string ConfigurationScope = "configuration";

    /// <summary>
    /// A configuration and everything it is for (PLAN.md §4.16): the worlds it tags, all of their
    /// zones and rooms, the templates those need, every ability, and the one configuration row
    /// with its canon. Null when no configuration has that key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The scope somebody moving a world actually wants.</b> "Everything" carries every other
    /// configuration on the server and every world that is none of this one's business; a world
    /// is one row of the five the Reaches are. This is the unit a story is authored in, and it
    /// is the unit that should travel.
    /// </para>
    /// <para>
    /// A tagged world that does not exist yet is simply absent, the same way a configuration may
    /// name a starting room that is not imported yet. Templates nothing in these worlds places
    /// are not carried; <see cref="ExportUnplacedAsync"/> is for those, and they belong to no
    /// configuration.
    /// </para>
    /// </remarks>
    public async Task<WorldBundle?> ExportConfigurationAsync(
        string configurationKey,
        CancellationToken cancellationToken)
    {
        var configuration = await db.GameConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == configurationKey, cancellationToken);

        if (configuration is null)
        {
            return null;
        }

        var worldKeys = configuration.WorldKeys;
        var zoneKeys = await db.Zones.AsNoTracking()
            .Where(z => worldKeys.Contains(z.WorldKey))
            .Select(z => z.Key)
            .ToListAsync(cancellationToken);

        return await BuildAsync(ConfigurationScope, configurationKey, zoneKeys, worldKeys, cancellationToken);
    }

    private async Task<WorldBundle> BuildAsync(
        string kind,
        string? key,
        IReadOnlyList<string> zoneKeys,
        IReadOnlyList<string> worldKeys,
        CancellationToken cancellationToken)
    {
        var worlds = await WorldsAsync(kind, key, zoneKeys, worldKeys, cancellationToken);
        var zones = await ZonesAsync(kind, zoneKeys, cancellationToken);
        var rooms = await RoomsAsync(kind, zoneKeys, cancellationToken);
        var spawners = await SpawnersAsync(kind, zoneKeys, cancellationToken);
        var quests = await QuestsAsync(kind, zoneKeys, cancellationToken);

        var (items, mobs) = await TemplatesAsync(kind, spawners, quests, cancellationToken);

        // Every ability, whatever the scope. An ability belongs to a Path rather than to a zone,
        // so there is nothing to filter it by - and a zone bundle that carried none would move a
        // crypt into an environment where the abilities meant to fight through it are whatever
        // that server happened to have.
        var abilities = await AbilitiesAsync(cancellationToken);

        // Every configuration too, and for the same reason: one belongs to a server rather than to
        // a zone. Which one is live is left behind deliberately - see BundleGameConfiguration.
        var configurations = await ConfigurationsAsync(kind, key, cancellationToken);

        // Only the sheets for the worlds this export carries. Unlike abilities and configurations
        // a map has an obvious owner, so a zone-scoped bundle takes the one world it names and no
        // others - and a world with nothing drawn for it simply contributes nothing.
        var maps = await MapsAsync(worlds, cancellationToken);

        return new WorldBundle(
            WorldBundle.CurrentFormatVersion,
            clock.GetUtcNow(),
            new BundleScope(kind, key),
            worlds,
            zones,
            rooms,
            items,
            mobs,
            abilities,
            spawners,
            quests,
            configurations,
            maps);
    }

    /// <summary>The rendered sheets belonging to the worlds in this export.</summary>
    private async Task<IReadOnlyList<BundleMap>> MapsAsync(
        IReadOnlyList<BundleWorld> worlds,
        CancellationToken cancellationToken)
    {
        var keys = worlds.Select(w => w.Key).ToList();

        return await db.WorldMaps.AsNoTracking()
            .Where(m => keys.Contains(m.WorldKey))
            .OrderBy(m => m.WorldKey)
            .Select(m => new BundleMap(m.WorldKey, m.Svg))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The abilities and nothing else, as a bundle that imports and merges like any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The return leg of how an ability is tuned.</b> Abilities are content now: they live in
    /// <c>content/abilities.json</c>, a builder retunes them in the editor against a running
    /// database, and the change has to come back to the file or the next fresh install loses it.
    /// Without this the only way back was to export the whole world and delete nine collections out
    /// of the JSON by hand — which is a step nobody does twice, so the file drifts from the
    /// database and neither is the source of truth any more.
    /// </para>
    /// <para>
    /// Not a filter on <see cref="ExportAsync"/> but its own method, because the scoped export
    /// closes over references and this closes over none: an ability names no room, no template and
    /// no quest, so there is nothing to reach for. The two are different shapes of question.
    /// </para>
    /// <para>
    /// Configurations are left out for the same reason worlds and zones are. A configuration is
    /// about a server and this file is about a Path; carrying one would mean a builder merging
    /// tuned abilities into content also moved a starting room they never looked at.
    /// </para>
    /// </remarks>
    public async Task<WorldBundle> ExportAbilitiesAsync(CancellationToken cancellationToken) =>
        new(WorldBundle.CurrentFormatVersion,
            clock.GetUtcNow(),
            new BundleScope(AbilitiesScope, null),
            [],
            [],
            [],
            [],
            [],
            await AbilitiesAsync(cancellationToken),
            [],
            [],
            []);

    /// <summary>
    /// The scope an abilities-only bundle declares.
    /// </summary>
    /// <remarks>
    /// Descriptive only. Nothing on the import path reads <c>scope</c> — an import is a merge of
    /// whatever collections a file holds — so this is here to tell a human what they are looking at
    /// in a file they saved six months ago, which is the whole job the field has ever done.
    /// </remarks>
    public const string AbilitiesScope = "abilities";

    /// <summary>
    /// One item template as a bundle carries it.
    /// </summary>
    /// <remarks>
    /// Shared by the scoped export and the unplaced one rather than written twice. Two copies of an
    /// eighteen-argument constructor is two places a new column has to be remembered, and the
    /// column that gets forgotten is the one nobody notices until an import drops it.
    /// </remarks>
    private static void Add(HashSet<string> set, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            set.Add(key);
        }
    }

    private static BundleItemTemplate ItemFor(Domain.Items.ItemTemplate i) => new(
        i.Key, i.Name, i.Description, i.Icon, i.Slots, i.IsTwoHanded, i.Weight, i.BaseValue,
        new Dictionary<string, object>(i.BaseStats),
        i.AttackDelayPulses, i.AttackVerb, i.IsQuestItem,
        i.IsLore, i.IsNoDrop, i.IsLightSource, i.FoodValue, i.DrinkValue, i.Paths);

    /// <inheritdoc cref="ItemFor"/>
    private static BundleMobTemplate MobFor(MobTemplate m) => new(
        m.Key, m.Name, m.Description, m.Icon, m.Level, m.WanderIntervalPulses,
        new Dictionary<string, object>(m.BaseStats),
        m.BaseXp, m.BaseGold,
        new Dictionary<string, object>(m.Behavior),
        [.. m.Loot],
        [.. m.Attacks]);

    /// <summary>The scope word for an export of the templates nothing places.</summary>
    public const string UnplacedScope = "unplaced";

    /// <summary>
    /// The item and mob templates no scoped export can reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap between "a bundle that stands up on its own" and "a snapshot of the world".</b> A
    /// scoped export closes over references, which is right — a zone bundle should carry what its
    /// zone needs and nothing else. But a template that nothing references yet is still real
    /// content: <c>spawn &lt;item|mob&gt; &lt;key&gt;</c> is a builder command, so an unplaced mob is
    /// one somebody can put down deliberately — a test fixture, or something held back for later.
    /// </para>
    /// <para>
    /// Those reach no scope at all, so a world rebuilt from scoped files alone loses them silently:
    /// the row goes and <c>spawn</c> starts refusing a key that worked yesterday. This is where they
    /// live instead. Merged with the scoped files it costs nothing — the union is what gets imported
    /// and an upsert of the same key twice is the same key — and alone it is the only way the
    /// snapshot is complete.
    /// </para>
    /// <para>
    /// Reachability is the same walk <see cref="TemplatesAsync"/> makes: spawners, quest
    /// giver/turn-in/required/reward, mob loot, and shopkeeper stock. It has to stay the same walk,
    /// or "unplaced" here means something different from "not carried" there.
    /// </para>
    /// </remarks>
    public async Task<WorldBundle> ExportUnplacedAsync(CancellationToken cancellationToken)
    {
        var spawners = await db.Spawners.AsNoTracking()
            .Select(s => new { s.TemplateKind, s.TemplateKey })
            .ToListAsync(cancellationToken);

        var quests = await db.Quests.AsNoTracking().ToListAsync(cancellationToken);
        var allMobs = await db.MobTemplates.AsNoTracking().OrderBy(m => m.Key).ToListAsync(cancellationToken);
        var allItems = await db.ItemTemplates.AsNoTracking().OrderBy(i => i.Key).ToListAsync(cancellationToken);

        var reachedMobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reachedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spawner in spawners)
        {
            (spawner.TemplateKind == TemplateKind.Mob ? reachedMobs : reachedItems)
                .Add(spawner.TemplateKey);
        }

        foreach (var quest in quests)
        {
            Add(reachedMobs, quest.GiverMobKey);
            Add(reachedMobs, quest.TurninMobKey);
            Add(reachedItems, quest.RequiredItemKey);
            Add(reachedItems, quest.RewardItemKey);
        }

        foreach (var mob in allMobs)
        {
            foreach (var entry in mob.Loot)
            {
                if (entry.TryGetValue("itemTemplateKey", out var value))
                {
                    Add(reachedItems, value?.ToString());
                }
            }

            foreach (var sold in Engine.Inhabitants.MobBehavior.SellsOf(mob.Behavior))
            {
                Add(reachedItems, sold);
            }
        }

        return new WorldBundle(
            WorldBundle.CurrentFormatVersion,
            clock.GetUtcNow(),
            new BundleScope(UnplacedScope, null),
            [],
            [],
            [],
            [.. allItems.Where(i => !reachedItems.Contains(i.Key)).Select(ItemFor)],
            [.. allMobs.Where(m => !reachedMobs.Contains(m.Key)).Select(MobFor)],
            [],
            [],
            [],
            []);
    }

    /// <remarks>
    /// The canon rides only in a full export. A scoped bundle is a realm's content file, meant to
    /// be read in review, and forty kilobytes of the same markdown in each of six files is not
    /// that; null there means "leave the stored one alone" on import.
    /// </remarks>
    private async Task<IReadOnlyList<BundleGameConfiguration>> ConfigurationsAsync(
        string kind,
        string? key,
        CancellationToken cancellationToken)
    {
        var query = db.GameConfigurations.AsNoTracking();

        // A configuration's own bundle carries that configuration and no other: the whole point
        // of the scope is that the rest of the server is none of its business.
        if (kind == ConfigurationScope)
        {
            query = query.Where(c => c.Key == key);
        }

        // Resolved, not stored: an empty row means "the text compiled into this server", and a
        // bundle that carried the emptiness would hand the receiving server's build the decision.
        // What travels is what the assist here is actually told (Assist.Canon.Resolve), so the
        // bundle is complete on its own - and importing it back fills the row with the same words
        // it was already reading.
        var carriesCanon = IsEverything(kind) || kind == ConfigurationScope;
        var configurations = await query.OrderBy(c => c.Key).ToListAsync(cancellationToken);

        return [.. configurations.Select(c => new BundleGameConfiguration(
            c.Key, c.Name, c.Description, c.StartingRoomKey, c.WelcomeMessage, c.BlockedWords,
            carriesCanon ? Assist.Canon.Resolve(c.Canon) : null,
            new List<string>(c.WorldKeys)))];
    }

    private async Task<IReadOnlyList<BundleAbility>> AbilitiesAsync(CancellationToken cancellationToken)
    {
        var abilities = await db.Abilities.AsNoTracking()
            .OrderBy(a => a.Path)
            .ThenBy(a => a.UnlockLevel)
            .ThenBy(a => a.Key)
            .ToListAsync(cancellationToken);

        return [.. abilities.Select(a => new BundleAbility(
            a.Key, a.Path, a.UnlockLevel, a.Name, a.Description, a.CostType, a.CostValue,
            a.CooldownPulses, a.CooldownGroup, a.CastTimePulses, a.TargetingType,
            [.. a.Effects.Select(e =>
                new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))]))];
    }

    // -----------------------------------------------------------------------
    // Scope
    // -----------------------------------------------------------------------

    /// <summary>
    /// Turns the two optional filters into a scope kind and the set of zones it covers. A zone
    /// key wins over a world key, since it is the narrower of the two and answering "both" with
    /// anything other than the narrower one would be a surprise.
    /// </summary>
    private async Task<(string Kind, string? Key, IReadOnlyList<string> ZoneKeys)?> ResolveScopeAsync(
        string? worldKey,
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            var exists = await db.Zones.AsNoTracking()
                .AnyAsync(z => z.Key == zoneKey, cancellationToken);

            return exists ? ("zone", zoneKey, new[] { zoneKey }) : null;
        }

        if (!string.IsNullOrWhiteSpace(worldKey))
        {
            var exists = await db.Worlds.AsNoTracking()
                .AnyAsync(w => w.Key == worldKey, cancellationToken);

            if (!exists)
            {
                return null;
            }

            var zones = await db.Zones.AsNoTracking()
                .Where(z => z.WorldKey == worldKey)
                .Select(z => z.Key)
                .ToListAsync(cancellationToken);

            return ("world", worldKey, zones);
        }

        return ("all", null, Array.Empty<string>());
    }

    private static bool IsEverything(string kind) => kind == "all";

    // -----------------------------------------------------------------------
    // Per entity kind
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<BundleWorld>> WorldsAsync(
        string kind,
        string? key,
        IReadOnlyList<string> zoneKeys,
        IReadOnlyList<string> worldKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Worlds.AsNoTracking();

        if (kind == "world")
        {
            query = query.Where(w => w.Key == key);
        }
        else if (kind == ConfigurationScope)
        {
            query = query.Where(w => worldKeys.Contains(w.Key));
        }
        else if (kind == "zone")
        {
            // The world above a zone travels with it. Importing a zone into an environment that
            // does not have its world would otherwise leave the zone parented to nothing, and
            // multipliers resolve through the world (§4.4) - so the numbers would be wrong
            // rather than merely missing.
            var owners = await db.Zones.AsNoTracking()
                .Where(z => zoneKeys.Contains(z.Key))
                .Select(z => z.WorldKey)
                .Distinct()
                .ToListAsync(cancellationToken);

            query = query.Where(w => owners.Contains(w.Key));
        }

        var worlds = await query.OrderBy(w => w.SortOrder).ThenBy(w => w.Key)
            .ToListAsync(cancellationToken);

        return [.. worlds.Select(w => new BundleWorld(
            w.Key, w.Name, w.Description, w.SortOrder, Flags(w.Flags), Multipliers(w.Multipliers)))];
    }

    private async Task<IReadOnlyList<BundleZone>> ZonesAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Zones.AsNoTracking();

        if (!IsEverything(kind))
        {
            query = query.Where(z => zoneKeys.Contains(z.Key));
        }

        var zones = await query.OrderBy(z => z.Key).ToListAsync(cancellationToken);

        return [.. zones.Select(z => new BundleZone(
            z.Key, z.WorldKey, z.Name, z.Description, z.MinLevel, z.MaxLevel,
            Flags(z.Flags), Multipliers(z.Multipliers)))];
    }

    private async Task<IReadOnlyList<BundleRoom>> RoomsAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Rooms.AsNoTracking().Include(r => r.Exits);

        var rooms = IsEverything(kind)
            ? await query.OrderBy(r => r.Key).ToListAsync(cancellationToken)
            : await query.Where(r => zoneKeys.Contains(r.ZoneKey))
                .OrderBy(r => r.Key)
                .ToListAsync(cancellationToken);

        return
        [
            .. rooms.Select(r => new BundleRoom(
                r.Key.ToString(),
                r.ZoneKey,
                r.Title,
                r.Description,
                Flags(r.Flags),
                r.Grid,
                r.Legend,
                r.EditorX,
                r.EditorY,
                [
                    // Ordered by the compass, not by insertion, so two exports of the same
                    // unchanged zone are byte-identical and a diff shows only real edits.
                    .. r.Exits
                        .OrderBy(e => DirectionExtensions.All.ToList().IndexOf(e.Direction))
                        .Select(e => new BundleExit(
                            e.Direction.ToLowerName(),
                            e.ToRoomKey.ToString(),
                            e.RequiredFlagKey,
                            e.RequiredItemKey,
                            e.RefusalMessage)),
                ])),
        ];
    }

    private async Task<IReadOnlyList<BundleSpawner>> SpawnersAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Spawners.AsNoTracking();

        if (!IsEverything(kind))
        {
            query = query.Where(s => zoneKeys.Contains(s.ZoneKey));
        }

        var spawners = await query.OrderBy(s => s.Id).ToListAsync(cancellationToken);

        return [.. spawners.Select(s => new BundleSpawner(
            s.Id, s.ZoneKey, s.TemplateKey, s.TemplateKind,
            [.. s.RoomKeys], s.TargetCount, s.RespawnSeconds, s.Wanders, s.FightsAtLevel,
            s.NameModifier))];
    }

    private async Task<IReadOnlyList<BundleQuest>> QuestsAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Quests.AsNoTracking();

        if (!IsEverything(kind))
        {
            query = query.Where(q => zoneKeys.Contains(q.ZoneKey));
        }

        var quests = await query.OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        return [.. quests.Select(q => new BundleQuest(
            q.Key, q.ZoneKey, q.Name, q.Summary, q.Description,
            q.GiverMobKey, q.TurninMobKey, q.RequiredItemKey, q.RequiredCount,
            q.RewardXp, q.RewardGold, q.RewardItemKey, q.RewardItemCount, q.RewardFlagKey,
            [.. q.PrerequisiteQuestKeys], q.IsRepeatable, q.AutoStart, [.. q.Paths],
            new Dictionary<string, string>(q.Dialogue, StringComparer.Ordinal), q.SortOrder))];
    }

    /// <summary>
    /// The templates a scoped bundle needs to stand up on its own. See the class remarks for why
    /// this is a closure rather than a filter.
    /// </summary>
    private async Task<(IReadOnlyList<BundleItemTemplate> Items, IReadOnlyList<BundleMobTemplate> Mobs)>
        TemplatesAsync(
            string kind,
            IReadOnlyList<BundleSpawner> spawners,
            IReadOnlyList<BundleQuest> quests,
            CancellationToken cancellationToken)
    {
        var mobKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!IsEverything(kind))
        {
            foreach (var spawner in spawners)
            {
                (spawner.TemplateKind == TemplateKind.Mob ? mobKeys : itemKeys)
                    .Add(spawner.TemplateKey);
            }

            foreach (var quest in quests)
            {
                Add(mobKeys, quest.GiverMobKey);
                Add(mobKeys, quest.TurninMobKey);
                Add(itemKeys, quest.RequiredItemKey);
                Add(itemKeys, quest.RewardItemKey);
            }
        }

        var mobQuery = db.MobTemplates.AsNoTracking();
        if (!IsEverything(kind))
        {
            mobQuery = mobQuery.Where(m => mobKeys.Contains(m.Key));
        }

        var mobs = await mobQuery.OrderBy(m => m.Key).ToListAsync(cancellationToken);

        // Loot and shop stock are read after the mobs are known, because most of what a zone's items
        // are reached through is a mob rather than a spawner.
        //
        // Loot: a required quest item usually arrives through a drop rather than through a spawner -
        // a zone bundle without it imports a quest nothing can finish, which is exactly the silent
        // failure §10 names.
        //
        // Stock: a shopkeeper's `sells` list names item keys and nothing else references them, so a
        // zone bundle without them imports shopkeepers whose entire inventory does not exist.
        // Gatetown is the case that showed it - sixteen item templates, no item spawner among them,
        // every one of them reached only by a `sells` list - and this hop was missing, so a fresh
        // export of that zone carried zero items and the file in content/ (written before the
        // closure was) still carried all sixteen. The two disagreed and neither was obviously wrong.
        if (!IsEverything(kind))
        {
            foreach (var mob in mobs)
            {
                foreach (var entry in mob.Loot)
                {
                    if (entry.TryGetValue("itemTemplateKey", out var value))
                    {
                        Add(itemKeys, value?.ToString());
                    }
                }

                foreach (var sold in MobBehavior.SellsOf(mob.Behavior))
                {
                    Add(itemKeys, sold);
                }
            }
        }

        var itemQuery = db.ItemTemplates.AsNoTracking();
        if (!IsEverything(kind))
        {
            itemQuery = itemQuery.Where(i => itemKeys.Contains(i.Key));
        }

        var items = await itemQuery.OrderBy(i => i.Key).ToListAsync(cancellationToken);

        return ([.. items.Select(ItemFor)], [.. mobs.Select(MobFor)]);

        static void Add(HashSet<string> set, string? key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                set.Add(key);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Value shaping
    // -----------------------------------------------------------------------

    /// <summary>
    /// A flag map as raw JSON, through the same serialiser the database column uses - so an
    /// unrecognised flag comes out byte-identical rather than being flattened to the booleans
    /// this build happens to know (§4.10).
    /// </summary>
    internal static JsonElement Flags(FlagSet? flags) =>
        JsonDocument.Parse(FlagSetJson.Serialize(flags)).RootElement.Clone();

    internal static IReadOnlyDictionary<string, decimal> Multipliers(Multipliers m) =>
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["strength"] = m.Strength,
            ["health"] = m.Health,
            ["damage"] = m.Damage,
            ["xp"] = m.Xp,
            ["gold"] = m.Gold,
            ["itemValue"] = m.ItemValue,
        };
}

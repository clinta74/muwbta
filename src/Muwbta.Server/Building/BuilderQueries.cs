using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Building;

/// <summary>
/// Everything the builder <em>reads</em>, served from Postgres rather than from the loop's
/// in-memory world.
/// </summary>
/// <remarks>
/// This is not an oversight, it is the single-writer rule (PLAN.md §2.1) applied to reads.
/// <c>WorldState</c> is mutated by the loop thread with no locks; enumerating its dictionaries
/// from a request thread is a genuine race that would surface as an occasional
/// InvalidOperationException under exactly the conditions - a builder editing while the world
/// is busy - that the builder exists for.
///
/// Reading from the database instead is safe and correct, because every mutation is persisted
/// before its HTTP call returns. The database is at most one in-flight edit behind, and an
/// in-flight edit is one the builder has not been told succeeded yet.
/// </remarks>
public sealed class BuilderQueries(MuwbtaDbContext db)
{
    public async Task<IReadOnlyList<WorldResponse>> WorldsAsync(CancellationToken cancellationToken)
    {
        var worlds = await db.Worlds.AsNoTracking()
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Key)
            .ToListAsync(cancellationToken);

        var counts = await db.Zones.AsNoTracking()
            .GroupBy(z => z.WorldKey)
            .Select(g => new { WorldKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorldKey, x => x.Count, cancellationToken);

        return [.. worlds.Select(w => WorldResponse.From(w, counts.GetValueOrDefault(w.Key)))];
    }

    public async Task<WorldResponse?> WorldAsync(string key, CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Key == key, cancellationToken);

        if (world is null)
        {
            return null;
        }

        var zones = await db.Zones.CountAsync(z => z.WorldKey == key, cancellationToken);
        return WorldResponse.From(world, zones);
    }

    public async Task<IReadOnlyList<ZoneResponse>> ZonesAsync(
        string? worldKey,
        CancellationToken cancellationToken)
    {
        var query = db.Zones.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(worldKey))
        {
            query = query.Where(z => z.WorldKey == worldKey);
        }

        var zones = await query.OrderBy(z => z.Key).ToListAsync(cancellationToken);

        var counts = await db.Rooms.AsNoTracking()
            .GroupBy(r => r.ZoneKey)
            .Select(g => new { ZoneKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneKey, x => x.Count, cancellationToken);

        return [.. zones.Select(z => ZoneResponse.From(z, counts.GetValueOrDefault(z.Key)))];
    }

    public async Task<ZoneResponse?> ZoneAsync(string key, CancellationToken cancellationToken)
    {
        var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Key == key, cancellationToken);

        if (zone is null)
        {
            return null;
        }

        var rooms = await db.Rooms.CountAsync(r => r.ZoneKey == key, cancellationToken);
        return ZoneResponse.From(zone, rooms);
    }

    public async Task<IReadOnlyList<RoomResponse>> RoomsAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var rooms = await db.Rooms.AsNoTracking()
            .Include(r => r.Exits)
            .Where(r => r.ZoneKey == zoneKey)
            .OrderBy(r => r.Key)
            .ToListAsync(cancellationToken);

        if (rooms.Count == 0)
        {
            return [];
        }

        var (zoneFlags, worldFlags) = await InheritedAsync(zoneKey, cancellationToken);

        // One set lookup for every exit target in the zone, rather than a query per exit:
        // "does this exit dangle?" is asked for every room on every canvas open.
        var targets = rooms.SelectMany(r => r.Exits).Select(e => e.ToRoomKey).Distinct().ToList();
        var existing = await ExistingRoomsAsync(targets, cancellationToken);

        return [.. rooms.Select(r => Project(r, zoneFlags, worldFlags, existing))];
    }

    public async Task<RoomResponse?> RoomAsync(RoomKey key, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking()
            .Include(r => r.Exits)
            .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);

        if (room is null)
        {
            return null;
        }

        var (zoneFlags, worldFlags) = await InheritedAsync(room.ZoneKey, cancellationToken);
        var existing = await ExistingRoomsAsync(
            [.. room.Exits.Select(e => e.ToRoomKey)], cancellationToken);

        return Project(room, zoneFlags, worldFlags, existing);
    }

    public async Task<IReadOnlyList<UnfinishedRoom>> UnfinishedAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var rooms = await db.Rooms.AsNoTracking()
            .Where(r => r.ZoneKey == zoneKey)
            .OrderBy(r => r.Key)
            .ToListAsync(cancellationToken);

        // Filtered in memory rather than with a jsonb containment predicate: the set is one
        // zone's worth of rooms, and keeping the flag semantics in one place (BooleanOrNull,
        // which also handles a wrong-typed value) is worth more than pushing it into SQL.
        return
        [
            .. rooms
                .Where(r => r.Flags.BooleanOrNull(RoomFlags.Unfinished.Key) == true)
                .Select(r => new UnfinishedRoom(r.Key.ToString(), r.Title, r.EditorX, r.EditorY)),
        ];
    }

    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(
        string? entityKind,
        string? entityKey,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.ContentAudits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityKind))
        {
            query = query.Where(a => a.EntityKind == entityKind);
        }

        if (!string.IsNullOrWhiteSpace(entityKey))
        {
            query = query.Where(a => a.EntityKey == entityKey);
        }

        var rows = await query
            .OrderByDescending(a => a.At)
            .ThenByDescending(a => a.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        var accountIds = rows.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).Distinct();
        var names = await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Username, cancellationToken);

        return
        [
            .. rows.Select(a => new AuditEntry(
                a.Id,
                a.AccountId,
                a.AccountId.HasValue ? names.GetValueOrDefault(a.AccountId.Value) : null,
                a.EntityKind,
                a.EntityKey,
                a.Action.ToString(),
                a.At)),
        ];
    }

    // -----------------------------------------------------------------------
    // Templates and Spawners (Phase 3)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every ability, each carrying whatever the validator says about it.
    /// </summary>
    /// <remarks>
    /// Validated on read, not only on write. A row can reach the table from an import, from a
    /// migration backfill, or from a build older than the check that would now refuse it — and in
    /// none of those cases did anybody see a save-time error. Since every way an ability can be
    /// broken is silent in play, the list is the only place a builder would ever find out.
    /// </remarks>
    public async Task<IReadOnlyList<AbilityResponse>> AbilitiesAsync(
        EffectRegistry effects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effects);

        var abilities = await db.Abilities.AsNoTracking()
            .OrderBy(a => a.Path)
            .ThenBy(a => a.UnlockLevel)
            .ToListAsync(cancellationToken);

        var setProblems = AbilityValidator.ValidateSet(abilities, effects);

        return [.. abilities.Select(a => ToResponse(
            a,
            setProblems.Where(p => p.Key == a.Key)))];
    }

    public async Task<AbilityResponse?> AbilityAsync(
        string key,
        EffectRegistry effects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effects);

        var ability = await db.Abilities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == key, cancellationToken);

        return ability is null
            ? null
            : ToResponse(ability, AbilityValidator.ValidateOne(ability, effects));
    }

    private static AbilityResponse ToResponse(
        Domain.Abilities.Ability ability,
        IEnumerable<AbilityProblem> problems) =>
        new(ability.Key,
            ability.Path,
            ability.UnlockLevel,
            ability.Name,
            ability.Description,
            ability.CostType,
            ability.CostValue,
            ability.CooldownPulses,
            ability.CooldownGroup,
            ability.CastTimePulses,
            ability.TargetingType,
            [.. ability.Effects],
            [.. problems.Select(p => new AbilityProblemResponse(p.Severity.ToString(), p.Message))]);

    public async Task<IReadOnlyList<MobTemplateResponse>> MobTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var templates = await db.MobTemplates.AsNoTracking().OrderBy(t => t.Key).ToListAsync(cancellationToken);
        return [.. templates.Select(t => new MobTemplateResponse(
            t.Key, t.Name, t.Description, t.Icon, t.Level, t.WanderIntervalPulses,
            new Dictionary<string, object>(t.BaseStats),
            t.BaseXp, t.BaseGold,
            new Dictionary<string, object>(t.Behavior),
            new List<Dictionary<string, object>>(t.Loot),
            new List<MobAttack>(t.Attacks)))];
    }

    public async Task<MobTemplateResponse?> MobTemplateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var template = await db.MobTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (template is null)
        {
            return null;
        }
        return new MobTemplateResponse(
            template.Key, template.Name, template.Description, template.Icon, template.Level, template.WanderIntervalPulses,
            new Dictionary<string, object>(template.BaseStats),
            template.BaseXp, template.BaseGold,
            new Dictionary<string, object>(template.Behavior),
            new List<Dictionary<string, object>>(template.Loot),
            new List<MobAttack>(template.Attacks));
    }

    public async Task<IReadOnlyList<ItemTemplateResponse>> ItemTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var templates = await db.ItemTemplates.AsNoTracking().OrderBy(t => t.Key).ToListAsync(cancellationToken);
        return [.. templates.Select(t => new ItemTemplateResponse(
            t.Key, t.Name, t.Description, t.Icon, t.Slots, t.IsTwoHanded, t.Weight, t.BaseValue,
            new Dictionary<string, object>(t.BaseStats),
            t.AttackDelayPulses, t.AttackVerb, t.IsQuestItem,
            t.IsLore, t.IsNoDrop, t.IsLightSource, t.FoodValue, t.DrinkValue, t.Paths))];
    }

    public async Task<ItemTemplateResponse?> ItemTemplateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var template = await db.ItemTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (template is null)
        {
            return null;
        }
        return new ItemTemplateResponse(
            template.Key, template.Name, template.Description, template.Icon, template.Slots,
            template.IsTwoHanded, template.Weight, template.BaseValue,
            new Dictionary<string, object>(template.BaseStats),
            template.AttackDelayPulses, template.AttackVerb, template.IsQuestItem,
            template.IsLore, template.IsNoDrop, template.IsLightSource,
            template.FoodValue, template.DrinkValue, template.Paths);
    }

    public async Task<IReadOnlyList<SpawnerResponse>> SpawnersAsync(
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        var query = db.Spawners.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            query = query.Where(s => s.ZoneKey == zoneKey);
        }

        var spawners = await query.OrderBy(s => s.Id).ToListAsync(cancellationToken);
        var facts = await SpawnFactsAsync(spawners, cancellationToken);

        return [.. spawners.Select(s => Response(s, facts))];
    }

    /// <summary>One spawner as the wire carries it, with what the server worked out about it.</summary>
    private static SpawnerResponse Response(
        Muwbta.Domain.Spawning.Spawner s,
        IReadOnlyDictionary<Guid, SpawnFacts> facts)
    {
        var fact = facts.GetValueOrDefault(s.Id);

        return new SpawnerResponse(
            s.Id, s.ZoneKey, s.TemplateKey, s.TemplateKind,
            new List<string>(s.RoomKeys), s.TargetCount, s.RespawnSeconds, WanderMode.From(s.Wanders),
            fact?.Level ?? 0, SpawnLevel.From(s.FightsAtLevel),
            s.NameModifier, fact?.SpawnsAs);
    }

    public async Task<SpawnerResponse?> SpawnerAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var spawner = await db.Spawners.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (spawner is null)
        {
            return null;
        }

        var facts = await SpawnFactsAsync([spawner], cancellationToken);

        return Response(spawner, facts);
    }

    /// <summary>
    /// What a mob spawner produces beyond what its row says: the level its mobs fight at and the
    /// name they appear under.
    /// </summary>
    private sealed record SpawnFacts(int Level, string SpawnsAs);

    /// <summary>
    /// The level each mob spawner's mobs will fight at, and the name they will carry, by spawner id.
    /// </summary>
    /// <remarks>
    /// <b>Computed here rather than in the client.</b> The whole argument for
    /// <see cref="Muwbta.Domain.Inhabitants.MobScaling"/> being one type is that a second answer to
    /// "what level is it" is how the label and the creature come apart; the browser already holds
    /// the templates and could do this arithmetic for free, and that is exactly the temptation to
    /// refuse.
    ///
    /// Batched into three round trips for the whole list — templates, zones, worlds — rather than
    /// three per row. Item spawners are skipped: an item has no level.
    /// </remarks>
    private async Task<Dictionary<Guid, SpawnFacts>> SpawnFactsAsync(
        IReadOnlyList<Muwbta.Domain.Spawning.Spawner> spawners,
        CancellationToken cancellationToken)
    {
        var mobs = spawners
            .Where(s => s.TemplateKind == Muwbta.Domain.Spawning.TemplateKind.Mob)
            .ToList();

        if (mobs.Count == 0)
        {
            return [];
        }

        var templateKeys = mobs.Select(s => s.TemplateKey).Distinct().ToList();
        var zoneKeys = mobs.Select(s => s.ZoneKey).Distinct().ToList();

        var templates = await db.MobTemplates.AsNoTracking()
            .Where(t => templateKeys.Contains(t.Key))
            .ToDictionaryAsync(t => t.Key, cancellationToken);

        var zones = await db.Zones.AsNoTracking()
            .Where(z => zoneKeys.Contains(z.Key))
            .ToDictionaryAsync(z => z.Key, cancellationToken);

        var worldKeys = zones.Values.Select(z => z.WorldKey).Distinct().ToList();
        var worlds = await db.Worlds.AsNoTracking()
            .Where(w => worldKeys.Contains(w.Key))
            .ToDictionaryAsync(w => w.Key, cancellationToken);

        var facts = new Dictionary<Guid, SpawnFacts>();

        foreach (var spawner in mobs)
        {
            // A spawner pointing at a deleted template goes dormant rather than throwing (§7.4);
            // reporting no level is the reading that matches.
            if (!templates.TryGetValue(spawner.TemplateKey, out var template) ||
                !zones.TryGetValue(spawner.ZoneKey, out var zone) ||
                !worlds.TryGetValue(zone.WorldKey, out var world))
            {
                continue;
            }

            // A pin replaces the zone's dials outright, so it is the answer rather than an input
            // to one - the same branch MobSpawner takes.
            var level = spawner.FightsAtLevel
                ?? Muwbta.Domain.Inhabitants.MobScaling
                    .FromZone(template.Level, world.Multipliers, zone.Multipliers, zone.MinLevel)
                    .Level;

            // The same composition the spawn path performs, so the browser shows the name the
            // room will - rather than a second rule for where the word goes.
            facts[spawner.Id] = new SpawnFacts(
                level,
                Muwbta.Domain.Inhabitants.MobNaming.Apply(template.Name, spawner.NameModifier));
        }

        return facts;
    }

    /// <summary>
    /// Everywhere one template shows up in the authored world (PLAN.md §7.9): the spawners that
    /// place it and the rooms they place it into, and — for an item — the mobs that drop it, the
    /// shopkeepers that sell it, and the quests that hand it over or ask for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A read rather than a new relationship.</b> Nothing here is stored: a spawner names a
    /// template, a loot row names an item, a quest names a reward. Each of those is visible from
    /// the side that does the naming and invisible from the side being named, which is the whole
    /// reason a template's own editor cannot answer "where does this exist" and this can.
    /// </para>
    /// <para>
    /// Batched into a fixed number of round trips rather than one per spawner or per mob. The two
    /// full-table reads — mob templates for their loot and stock — are the price of those living
    /// in jsonb, which is not a shape a <c>WHERE</c> can ask about, and it is the same read
    /// <c>/reachability</c> already makes for the same reason.
    /// </para>
    /// </remarks>
    /// <returns>Null when no template of that kind has this key, which the endpoint reports as 404.</returns>
    public async Task<TemplatePlacement?> PlacementAsync(
        TemplateKind kind,
        string templateKey,
        CancellationToken cancellationToken)
    {
        var exists = kind == TemplateKind.Mob
            ? await db.MobTemplates.AsNoTracking()
                .AnyAsync(t => t.Key == templateKey, cancellationToken)
            : await db.ItemTemplates.AsNoTracking()
                .AnyAsync(t => t.Key == templateKey, cancellationToken);

        if (!exists)
        {
            return null;
        }

        var spawners = await db.Spawners.AsNoTracking()
            .Where(s => s.TemplateKind == kind && s.TemplateKey == templateKey)
            .OrderBy(s => s.ZoneKey).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var placed = new List<PlacementSpawner>(spawners.Count);

        if (spawners.Count > 0)
        {
            var zoneKeys = spawners.Select(s => s.ZoneKey).Distinct().ToList();
            var zoneNames = await db.Zones.AsNoTracking()
                .Where(z => zoneKeys.Contains(z.Key))
                .ToDictionaryAsync(z => z.Key, z => z.Name, cancellationToken);

            // A spawner stores its rooms as strings, so a key that no longer parses - or no longer
            // resolves - has to survive as far as the response rather than being filtered out here.
            // "Placed in a room that is gone" is the finding, and dropping the row hides it.
            var parsed = new Dictionary<string, RoomKey>(StringComparer.Ordinal);
            foreach (var raw in spawners.SelectMany(s => s.RoomKeys).Distinct(StringComparer.Ordinal))
            {
                if (RoomKey.TryParse(raw, out var key))
                {
                    parsed[raw] = key;
                }
            }

            var candidates = parsed.Values.ToList();
            var titles = candidates.Count == 0
                ? []
                : await db.Rooms.AsNoTracking()
                    .Where(r => candidates.Contains(r.Key))
                    .ToDictionaryAsync(r => r.Key, r => r.Title, cancellationToken);

            var facts = await SpawnFactsAsync(spawners, cancellationToken);

            placed.AddRange(spawners.Select(s => new PlacementSpawner(
                s.Id,
                s.ZoneKey,
                zoneNames.GetValueOrDefault(s.ZoneKey, string.Empty),
                s.TargetCount,
                s.RespawnSeconds,
                facts.GetValueOrDefault(s.Id)?.Level ?? 0,
                [.. s.RoomKeys.Select(raw => new PlacementRoom(
                    raw,
                    parsed.TryGetValue(raw, out var key) && titles.TryGetValue(key, out var title)
                        ? title
                        : null))],
                // Only worth a word when the placement has one: the panel is already headed by
                // the template's name, so repeating it on every row would say nothing.
                s.NameModifier is null ? null : facts.GetValueOrDefault(s.Id)?.SpawnsAs)));
        }

        if (kind == TemplateKind.Mob)
        {
            return new TemplatePlacement(templateKey, "mob", placed, [], [], []);
        }

        var mobs = await db.MobTemplates.AsNoTracking().ToListAsync(cancellationToken);

        var spawnedMobs = (await db.Spawners.AsNoTracking()
                .Where(s => s.TemplateKind == TemplateKind.Mob)
                .Select(s => s.TemplateKey)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var droppedBy = mobs
            .Select(m => (Mob: m, Chance: LootTable.DropChance(m.Loot, templateKey)))
            .Where(x => x.Chance is not null)
            .OrderByDescending(x => x.Chance)
            .ThenBy(x => x.Mob.Key, StringComparer.Ordinal)
            .Select(x => new PlacementMob(
                x.Mob.Key, x.Mob.Name, spawnedMobs.Contains(x.Mob.Key), x.Chance))
            .ToList();

        // Through the engine's own reader, not a second one: `sells` is a jsonb list, and the
        // shop command resolves it through JsonBag for the same round-trip reasons LootTable
        // exists for.
        var soldBy = mobs
            .Where(m => Muwbta.Engine.Inhabitants.MobBehavior.SellsOf(m.Behavior)
                .Any(sold => string.Equals(sold, templateKey, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => new PlacementMob(m.Key, m.Name, spawnedMobs.Contains(m.Key)))
            .ToList();

        var quests = await db.Quests.AsNoTracking()
            .Where(q => q.RewardItemKey == templateKey || q.RequiredItemKey == templateKey)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        // A quest can both ask for a thing and hand it back, so this is two rows rather than one
        // with a compound role - "required" and "reward" are different facts about the same key.
        var questRows = new List<PlacementQuest>();
        foreach (var quest in quests)
        {
            if (string.Equals(quest.RewardItemKey, templateKey, StringComparison.OrdinalIgnoreCase))
            {
                questRows.Add(new PlacementQuest(quest.Key, quest.Name, quest.ZoneKey, "reward"));
            }

            if (string.Equals(quest.RequiredItemKey, templateKey, StringComparison.OrdinalIgnoreCase))
            {
                questRows.Add(new PlacementQuest(quest.Key, quest.Name, quest.ZoneKey, "required"));
            }
        }

        return new TemplatePlacement(templateKey, "item", placed, droppedBy, soldBy, questRows);
    }

    public async Task<IReadOnlyList<QuestResponse>> QuestsAsync(CancellationToken cancellationToken)
    {
        var quests = await db.Quests.AsNoTracking()
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        return [.. quests.Select(QuestResponse.From)];
    }

    public async Task<IReadOnlyList<QuestResponse>> QuestsByZoneAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var quests = await db.Quests.AsNoTracking()
            .Where(q => q.ZoneKey == zoneKey)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        return [.. quests.Select(QuestResponse.From)];
    }

    public async Task<QuestResponse?> QuestAsync(string key, CancellationToken cancellationToken)
    {
        var quest = await db.Quests.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Key == key, cancellationToken);

        if (quest is null)
        {
            return null;
        }

        return QuestResponse.From(quest);
    }

    /// <summary>
    /// Multiplier preview for a zone: shows how templates resolve with current multipliers.
    /// Used for difficulty tuning (PLAN.md §7.5).
    /// </summary>
    public async Task<MultiplierPreview?> PreviewAsync(string zoneKey, CancellationToken cancellationToken)
    {
        var zone = await db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Key == zoneKey, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        var world = await db.Worlds.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Key == zone.WorldKey, cancellationToken);
        if (world is null)
        {
            return null;
        }

        // Get all spawners for this zone to find unique templates
        var spawners = await db.Spawners.AsNoTracking()
            .Where(s => s.ZoneKey == zoneKey)
            .ToListAsync(cancellationToken);

        var mobTemplateKeys = spawners
            .Where(s => s.TemplateKind == Muwbta.Domain.Spawning.TemplateKind.Mob)
            .Select(s => s.TemplateKey)
            .Distinct()
            .ToList();

        var itemTemplateKeys = spawners
            .Where(s => s.TemplateKind == Muwbta.Domain.Spawning.TemplateKind.Item)
            .Select(s => s.TemplateKey)
            .Distinct()
            .ToList();

        var mobTemplates = await db.MobTemplates.AsNoTracking()
            .Where(t => mobTemplateKeys.Contains(t.Key))
            .ToListAsync(cancellationToken);

        var itemTemplates = await db.ItemTemplates.AsNoTracking()
            .Where(t => itemTemplateKeys.Contains(t.Key))
            .ToListAsync(cancellationToken);

        var rows = new List<MultiplierPreviewRow>();

        // Add mob templates
        foreach (var mob in mobTemplates.OrderBy(t => t.Key))
        {
            var scaling = Muwbta.Domain.Inhabitants.MobScaling.FromZone(
                mob.Level, world.Multipliers, zone.Multipliers, zone.MinLevel);

            rows.Add(new MultiplierPreviewRow(
                mob.Key,
                mob.Name,
                Muwbta.Domain.Spawning.TemplateKind.Mob,
                new Dictionary<string, object>(mob.BaseStats),
                ResolveMobStats(mob, scaling, world.Multipliers, zone.Multipliers),
                mob.Level,
                scaling.Level,
                // The same keys unscaled, so the panel's Base column lines up with its Resolved one
                // even where the template wrote its dice as a range.
                ResolveMobStats(mob, Unscaled(mob.Level), NoMultipliers, NoMultipliers)));
        }

        // Add item templates
        foreach (var item in itemTemplates.OrderBy(t => t.Key))
        {
            var resolved = ResolveItemStats(item, world.Multipliers, zone.Multipliers);
            rows.Add(new MultiplierPreviewRow(
                item.Key,
                item.Name,
                Muwbta.Domain.Spawning.TemplateKind.Item,
                new Dictionary<string, object>(item.BaseStats),
                resolved,
                TemplateLevel: 0,
                FightsAtLevel: 0,
                BaseValues: new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["value"] = item.BaseValue,
                }));
        }

        return new MultiplierPreview(
            zoneKey,
            MultipliersDictionary(world.Multipliers),
            MultipliersDictionary(zone.Multipliers),
            rows);
    }

    /// <summary>
    /// What a mob template resolves to in this zone, as the numbers the panel shows.
    /// </summary>
    /// <remarks>
    /// <b>Through <see cref="Muwbta.Domain.Inhabitants.MobScaling"/>, the same type the spawner
    /// uses.</b> This method previously did its own arithmetic — health through
    /// <c>MultiplierType.Strength</c> alone, and no damage at all — which was a second
    /// implementation of the resolution and had already drifted from the first: it missed the
    /// <c>health</c> dial entirely, and a preview that cannot disagree with the spawner is the
    /// only kind worth showing.
    ///
    /// Damage is reported as <c>damageMin</c>/<c>damageMax</c> whichever way the template wrote it,
    /// because the row's resolved values are integers and the <c>"4-7"</c> range form is a string.
    /// </remarks>
    /// <summary>Every dial at 1.0 — the identity, for reporting a template's unscaled numbers.</summary>
    private static Muwbta.Domain.Worlds.Multipliers NoMultipliers => new();

    /// <summary>The scaling that changes nothing, so one method can report both columns.</summary>
    private static Muwbta.Domain.Inhabitants.MobScaling Unscaled(int templateLevel) =>
        Muwbta.Domain.Inhabitants.MobScaling.FromZone(templateLevel, NoMultipliers, NoMultipliers, 1);

    private static Dictionary<string, int> ResolveMobStats(
        Muwbta.Domain.Inhabitants.MobTemplate template,
        Muwbta.Domain.Inhabitants.MobScaling scaling,
        Muwbta.Domain.Worlds.Multipliers worldMults,
        Muwbta.Domain.Worlds.Multipliers zoneMults)
    {
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);
        var stats = scaling.ResolveStats(template.BaseStats);

        // Read through StatReader for the reason the old comment here gave: BaseStats is jsonb, so
        // every value arrives as a JsonElement and `health is int` was false for every template
        // that had ever been saved - the preview reported 40 health for all of them.
        if (StatReader.TryReadInt(stats, "health", out var health))
        {
            resolved["health"] = health;
        }

        if (StatReader.TryReadRange(stats, "damage", out var min, out var max))
        {
            resolved["damageMin"] = min;
            resolved["damageMax"] = max;
        }

        if (StatReader.TryReadInt(stats, "damageMin", out var declaredMin))
        {
            resolved["damageMin"] = declaredMin;
        }

        if (StatReader.TryReadInt(stats, "damageMax", out var declaredMax))
        {
            resolved["damageMax"] = declaredMax;
        }

        // Xp and Gold are values scaled by their own dial rather than combat power, so they stay
        // on Multipliers.Resolve. MobScaling deliberately says nothing about them (§4.7).
        resolved["xp"] = Muwbta.Domain.Worlds.Multipliers.Resolve(
            template.BaseXp, worldMults, zoneMults, Muwbta.Domain.Worlds.MultiplierType.Xp);

        resolved["gold"] = Muwbta.Domain.Worlds.Multipliers.Resolve(
            template.BaseGold, worldMults, zoneMults, Muwbta.Domain.Worlds.MultiplierType.Gold);

        return resolved;
    }

    private static Dictionary<string, int> ResolveItemStats(
        Muwbta.Domain.Items.ItemTemplate template,
        Muwbta.Domain.Worlds.Multipliers worldMults,
        Muwbta.Domain.Worlds.Multipliers zoneMults)
    {
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);

        // Value: use ItemValue multiplier
        resolved["value"] = Muwbta.Domain.Worlds.Multipliers.Resolve(
            template.BaseValue, worldMults, zoneMults, Muwbta.Domain.Worlds.MultiplierType.ItemValue);

        return resolved;
    }

    private static Dictionary<string, decimal> MultipliersDictionary(Muwbta.Domain.Worlds.Multipliers mults)
    {
        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["strength"] = mults.Strength,
            ["health"] = mults.Health,
            ["damage"] = mults.Damage,
            ["xp"] = mults.Xp,
            ["gold"] = mults.Gold,
            ["itemValue"] = mults.ItemValue,
        };
    }

    // -----------------------------------------------------------------------
    // Validation (PLAN.md §7.4) - advisory only, never blocks a save
    // -----------------------------------------------------------------------

    public async Task<ZoneValidation> ValidateAsync(string zoneKey, CancellationToken cancellationToken)
    {
        var warnings = new List<ValidationWarning>();

        var rooms = await db.Rooms.AsNoTracking()
            .Include(r => r.Exits)
            .Where(r => r.ZoneKey == zoneKey)
            .ToListAsync(cancellationToken);

        if (rooms.Count == 0)
        {
            warnings.Add(new ValidationWarning("empty-zone", zoneKey, "This zone has no rooms."));
            return new ZoneValidation(zoneKey, warnings);
        }

        var (zoneFlags, worldFlags) = await InheritedAsync(zoneKey, cancellationToken);

        var targets = rooms.SelectMany(r => r.Exits).Select(e => e.ToRoomKey).Distinct().ToList();
        var existing = await ExistingRoomsAsync(targets, cancellationToken);

        foreach (var room in rooms.OrderBy(r => r.Key.ToString(), StringComparer.Ordinal))
        {
            foreach (var exit in room.Exits.Where(e => !existing.Contains(e.ToRoomKey)))
            {
                warnings.Add(new ValidationWarning(
                    "dangling-exit",
                    room.Key.ToString(),
                    $"{exit.Direction.ToLowerName()} points at '{exit.ToRoomKey}', which does not exist."));
            }

            foreach (var key in room.Flags.UnknownKeys)
            {
                warnings.Add(new ValidationWarning(
                    "unknown-flag",
                    room.Key.ToString(),
                    $"'{key}' is not a flag this server knows about. It is kept, but nothing reads it."));
            }

            // The one flag whose blast radius is larger than the thing you edited: setting pvp
            // on a zone makes every room in it lethal, including a town square somebody else
            // authored. Naming the rooms turns an invisible edit into a visible one (§7.4).
            var pvp = RoomFlags.Resolve(RoomFlags.Pvp, room.Flags, zoneFlags, worldFlags);
            if (pvp.Value && pvp.IsInherited)
            {
                warnings.Add(new ValidationWarning(
                    "inherited-pvp",
                    room.Key.ToString(),
                    $"PvP here, inherited from the {pvp.Source.ToString().ToLowerInvariant()}."));
            }

            if (string.IsNullOrWhiteSpace(room.Description))
            {
                warnings.Add(new ValidationWarning(
                    "no-description", room.Key.ToString(), "No description."));
            }

            if (room.Grid.Count > 0 && room.Grid.Select(row => row.Length).Distinct().Count() > 1)
            {
                warnings.Add(new ValidationWarning(
                    "ragged-grid",
                    room.Key.ToString(),
                    "Grid rows are different lengths; the map falls back to a plain rectangle."));
            }

            foreach (var glyph in room.Grid
                .SelectMany(row => row.Select(c => c.ToString()))
                .Distinct(StringComparer.Ordinal)
                .Where(g => !room.Legend.ContainsKey(g)))
            {
                warnings.Add(new ValidationWarning(
                    "legend-gap", room.Key.ToString(), $"Grid uses '{glyph}' with no legend entry."));
            }
        }

        // Orphans: no way in. Computed across the whole world, not just this zone, because a
        // zone entrance is normally reached from its neighbour.
        var inbound = await db.RoomExits.AsNoTracking()
            .Select(e => e.ToRoomKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        var reachable = inbound.ToHashSet();

        foreach (var room in rooms.Where(r => !reachable.Contains(r.Key)))
        {
            warnings.Add(new ValidationWarning(
                "orphan-room", room.Key.ToString(), "Nothing links to this room."));
        }

        warnings.AddRange(await LevelWarningsAsync(zoneKey, cancellationToken));
        warnings.AddRange(await GateWarningsAsync(rooms, zoneFlags, worldFlags, cancellationToken));

        return new ZoneValidation(zoneKey, warnings);
    }

    /// <summary>
    /// What is wrong with the conditional exits in this zone (PLAN.md §4.15).
    /// </summary>
    /// <remarks>
    /// <b>This is what stands in for a registry of character flags.</b> There is deliberately no
    /// closed list of them — which flags are real is a property of the authored world, not of the
    /// binary — so a mistyped flag key cannot be caught by a lookup. It is caught here instead, by
    /// nothing being able to grant it: the same check, and the same class of bug, as a quest item
    /// that nothing drops.
    /// </remarks>
    private async Task<IReadOnlyList<ValidationWarning>> GateWarningsAsync(
        IReadOnlyList<Room> rooms,
        FlagSet? zoneFlags,
        FlagSet? worldFlags,
        CancellationToken cancellationToken)
    {
        var conditional = rooms
            .SelectMany(r => r.Exits.Where(e => e.IsConditional).Select(e => (Room: r, Exit: e)))
            .ToList();

        if (conditional.Count == 0)
        {
            return [];
        }

        var warnings = new List<ValidationWarning>();

        var granted = await db.Quests.AsNoTracking()
            .Where(q => q.RewardFlagKey != null)
            .Select(q => q.RewardFlagKey!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var grantable = granted.ToHashSet(StringComparer.Ordinal);

        var neededItems = conditional
            .Select(c => c.Exit.RequiredItemKey)
            .Where(k => k is not null)
            .Select(k => k!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var realItems = (await db.ItemTemplates.AsNoTracking()
                .Where(i => neededItems.Contains(i.Key))
                .Select(i => i.Key)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (room, exit) in conditional.OrderBy(c => c.Room.Key.ToString(), StringComparer.Ordinal))
        {
            var where = room.Key.ToString();
            var which = exit.Direction.ToLowerName();

            if (exit.RequiredFlagKey is { } flag && !grantable.Contains(flag))
            {
                warnings.Add(new ValidationWarning(
                    "ungrantable-gate",
                    where,
                    $"{which} needs the flag '{flag}', which no quest grants. Nobody can pass."));
            }

            if (exit.RequiredItemKey is { } item && !realItems.Contains(item))
            {
                warnings.Add(new ValidationWarning(
                    "missing-gate-item",
                    where,
                    $"{which} needs the item '{item}', which does not exist. Nobody can pass."));
            }

            // A bind point behind a lock lets a character recall past it forever after, because
            // recall teleports rather than walks (§4.12, §4.15). Hub zones are the intended home
            // for `respawn` and are never gated, so this fires on a mistake rather than a design.
            if (RoomFlags.Resolve(RoomFlags.Respawn, room.Flags, zoneFlags, worldFlags).Value)
            {
                warnings.Add(new ValidationWarning(
                    "bind-behind-gate",
                    where,
                    $"Bindable, and {which} is gated. A character can bind here and recall back in without passing it."));
            }
        }

        return warnings;
    }

    /// <summary>
    /// What the levels in this zone look like from outside (PLAN.md §4.7).
    /// </summary>
    /// <remarks>
    /// <b>All advisory, per §7.4.</b> None of these is wrong — a deliberately trivial critter in a
    /// hard zone, a boss above the band, and a mob that pays little for a hard fight are all
    /// legitimate authoring. They are here because each is also what an accident looks like, and
    /// the difference is only visible to whoever wrote it.
    ///
    /// The last one is the honest cost of the pin saying nothing about experience: lifting a rat to
    /// 27 leaves it paying a rat's reward. That is a deliberate trade (§4.7), and a trade nobody is
    /// told about is just a trap.
    /// </remarks>
    private async Task<List<ValidationWarning>> LevelWarningsAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var warnings = new List<ValidationWarning>();

        var zone = await db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Key == zoneKey, cancellationToken);

        if (zone is null)
        {
            return warnings;
        }

        var spawners = await db.Spawners.AsNoTracking()
            .Where(s => s.ZoneKey == zoneKey && s.TemplateKind == Muwbta.Domain.Spawning.TemplateKind.Mob)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        if (spawners.Count == 0)
        {
            return warnings;
        }

        var facts = await SpawnFactsAsync(spawners, cancellationToken);
        var keys = spawners.Select(s => s.TemplateKey).Distinct().ToList();
        var templates = await db.MobTemplates.AsNoTracking()
            .Where(t => keys.Contains(t.Key))
            .ToDictionaryAsync(t => t.Key, cancellationToken);

        foreach (var spawner in spawners)
        {
            if (!facts.TryGetValue(spawner.Id, out var fact) ||
                !templates.TryGetValue(spawner.TemplateKey, out var template))
            {
                continue;
            }

            var level = fact.Level;

            if (level < zone.MinLevel || level > zone.MaxLevel)
            {
                warnings.Add(new ValidationWarning(
                    "level-outside-band",
                    spawner.TemplateKey,
                    $"Fights at level {level}, outside this zone's {zone.MinLevel}–{zone.MaxLevel} band."));
            }

            if (level > XpProgression.MaxLevel)
            {
                warnings.Add(new ValidationWarning(
                    "level-above-cap",
                    spawner.TemplateKey,
                    $"Fights at level {level}, above the level {XpProgression.MaxLevel} a character can reach."));
            }

            // A mob two tiers above the experience it pays. The threshold is deliberately loose:
            // this is meant to catch a template lifted a long way and never re-costed, not to
            // second-guess tuning.
            if (spawner.FightsAtLevel is not null && template.Level > 0 && level >= template.Level * 2)
            {
                warnings.Add(new ValidationWarning(
                    "reward-lags-level",
                    spawner.TemplateKey,
                    $"Pinned to level {level} from a level {template.Level} template, but still pays "
                    + $"{template.BaseXp} experience. A pin scales health and damage, never the reward."));
            }
        }

        return warnings;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<(FlagSet? Zone, FlagSet? World)> InheritedAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var zone = await db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Key == zoneKey, cancellationToken);

        if (zone is null)
        {
            return (null, null);
        }

        var world = await db.Worlds.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Key == zone.WorldKey, cancellationToken);

        return (zone.Flags, world?.Flags);
    }

    private async Task<HashSet<RoomKey>> ExistingRoomsAsync(
        IReadOnlyList<RoomKey> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var found = await db.Rooms.AsNoTracking()
            .Where(r => candidates.Contains(r.Key))
            .Select(r => r.Key)
            .ToListAsync(cancellationToken);

        return [.. found];
    }

    private static RoomResponse Project(
        Room room,
        FlagSet? zoneFlags,
        FlagSet? worldFlags,
        HashSet<RoomKey> existingTargets) =>
        new(
            room.Key.ToString(),
            room.ZoneKey,
            room.Title,
            room.Description,
            WorldResponse.Flat(room.Flags),
            [
                .. RoomFlags.All.Select(
                    flag => ResolvedFlag.For(flag, room.Flags, zoneFlags, worldFlags)),
            ],
            room.Grid,
            room.Legend,
            room.EditorX,
            room.EditorY,
            [
                .. room.Exits
                    .OrderBy(e => DirectionExtensions.All.ToList().IndexOf(e.Direction))
                    .Select(e => new ExitResponse(
                        e.Direction.ToLowerName(),
                        e.ToRoomKey.ToString(),
                        existingTargets.Contains(e.ToRoomKey),
                        e.RequiredFlagKey,
                        e.RequiredItemKey,
                        e.RefusalMessage)),
            ]);
}

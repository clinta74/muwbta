using System.Text.Json;
using System.Text.Json.Nodes;
using Muwbta.Domain.Building;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Persistence;
using Muwbta.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Building;

/// <summary>
/// Replays the primitives the game loop applied to memory into Postgres, writing a
/// <see cref="ContentAudit"/> row for each (PLAN.md §7.3).
/// </summary>
/// <remarks>
/// This runs on the request thread, never on the loop - §2.1 forbids database work there.
/// It deliberately loads its own entity instances rather than sharing the loop's: the loop's
/// world is a long-lived singleton it mutates freely, and handing those objects to a change
/// tracker on another thread is the data race the single-writer rule exists to prevent.
///
/// The whole primitive list runs in one transaction with a save per step. Saving per step
/// preserves the order the loop applied them in - which matters for a rename, where the new
/// room must exist before exits can point at it - and the transaction means a failure halfway
/// through leaves nothing behind.
/// </remarks>
public sealed class WorldWriter(MuwbtaDbContext db, TimeProvider clock)
{
    public async Task WriteAsync(
        IReadOnlyList<WorldChange> changes,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var change in changes)
        {
            var before = await CaptureAsync(change, cancellationToken);
            var action = await ApplyAsync(change, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            var after = action == ContentAction.Delete
                ? null
                : await CaptureAsync(change, cancellationToken);

            db.ContentAudits.Add(new ContentAudit
            {
                AccountId = accountId,
                EntityKind = change.EntityKind,
                EntityKey = change.EntityKey,
                Action = action,
                Before = before,
                After = after,
                At = now,
            });

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Applying
    // -----------------------------------------------------------------------

    private async Task<ContentAction> ApplyAsync(WorldChange change, CancellationToken cancellationToken)
    {
        switch (change)
        {
            case UpsertWorld c:
            {
                var entity = await db.Worlds.FirstOrDefaultAsync(w => w.Key == c.Key, cancellationToken);

                if (entity is null)
                {
                    db.Worlds.Add(new World
                    {
                        Key = c.Key,
                        Name = c.Name,
                        Description = c.Description,
                        SortOrder = c.SortOrder,
                        Flags = c.Flags.Clone(),
                        Multipliers = c.Multipliers.Clone(),
                    });

                    return ContentAction.Create;
                }

                entity.Name = c.Name;
                entity.Description = c.Description;
                entity.SortOrder = c.SortOrder;
                entity.Flags = c.Flags.Clone();
                entity.Multipliers = c.Multipliers.Clone();
                return ContentAction.Update;
            }

            case DeleteWorld c:
            {
                var entity = await db.Worlds.FirstOrDefaultAsync(w => w.Key == c.Key, cancellationToken);
                if (entity is not null)
                {
                    db.Worlds.Remove(entity);
                }

                // By hand, because world_maps carries no foreign key to lean on - see
                // WorldMapConfiguration for why it does not. A sheet outliving its world would be
                // served to nobody and exported into the next bundle, which is how orphans travel.
                var orphan = await db.WorldMaps.FirstOrDefaultAsync(m => m.WorldKey == c.Key, cancellationToken);
                if (orphan is not null)
                {
                    db.WorldMaps.Remove(orphan);
                }

                return ContentAction.Delete;
            }

            case SetWorldMap c:
            {
                var entity = await db.WorldMaps.FirstOrDefaultAsync(m => m.WorldKey == c.Key, cancellationToken);

                if (c.Svg is null)
                {
                    if (entity is not null)
                    {
                        db.WorldMaps.Remove(entity);
                    }

                    return ContentAction.Delete;
                }

                if (entity is null)
                {
                    db.WorldMaps.Add(new WorldMap { WorldKey = c.Key, Svg = c.Svg });
                    return ContentAction.Create;
                }

                entity.Svg = c.Svg;
                return ContentAction.Update;
            }

            case UpsertZone c:
            {
                var entity = await db.Zones.FirstOrDefaultAsync(z => z.Key == c.Key, cancellationToken);

                if (entity is null)
                {
                    db.Zones.Add(new Zone
                    {
                        Key = c.Key,
                        WorldKey = c.WorldKey,
                        Name = c.Name,
                        Description = c.Description,
                        MinLevel = c.MinLevel,
                        MaxLevel = c.MaxLevel,
                        Flags = c.Flags.Clone(),
                        Multipliers = c.Multipliers.Clone(),
                    });

                    return ContentAction.Create;
                }

                entity.Name = c.Name;
                entity.Description = c.Description;
                entity.MinLevel = c.MinLevel;
                entity.MaxLevel = c.MaxLevel;
                entity.Flags = c.Flags.Clone();
                entity.Multipliers = c.Multipliers.Clone();
                return ContentAction.Update;
            }

            case DeleteZone c:
            {
                var entity = await db.Zones.FirstOrDefaultAsync(z => z.Key == c.Key, cancellationToken);
                if (entity is not null)
                {
                    db.Zones.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertRoom c:
            {
                var entity = await db.Rooms.FirstOrDefaultAsync(r => r.Key == c.Key, cancellationToken);

                if (entity is null)
                {
                    db.Rooms.Add(new Room
                    {
                        Key = c.Key,
                        ZoneKey = c.ZoneKey,
                        Title = c.Title,
                        Description = c.Description,
                        Flags = c.Flags.Clone(),
                        Grid = [.. c.Grid],
                        Legend = new Dictionary<string, string>(c.Legend, StringComparer.Ordinal),
                        EditorX = c.EditorX,
                        EditorY = c.EditorY,
                    });

                    return ContentAction.Create;
                }

                entity.Title = c.Title;
                entity.Description = c.Description;
                entity.Flags = c.Flags.Clone();
                entity.Grid = [.. c.Grid];
                entity.Legend = new Dictionary<string, string>(c.Legend, StringComparer.Ordinal);
                entity.EditorX = c.EditorX;
                entity.EditorY = c.EditorY;
                return ContentAction.Update;
            }

            case DeleteRoom c:
            {
                var entity = await db.Rooms.FirstOrDefaultAsync(r => r.Key == c.Key, cancellationToken);
                if (entity is not null)
                {
                    db.Rooms.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case SetExit c:
            {
                var entity = await db.RoomExits.FirstOrDefaultAsync(
                    e => e.FromRoomKey == c.From && e.Direction == c.Direction,
                    cancellationToken);

                if (entity is null)
                {
                    db.RoomExits.Add(new RoomExit
                    {
                        FromRoomKey = c.From,
                        Direction = c.Direction,
                        ToRoomKey = c.To,
                        RequiredFlagKey = c.RequiredFlagKey,
                        RequiredItemKey = c.RequiredItemKey,
                        RefusalMessage = c.RefusalMessage,
                    });

                    return ContentAction.Create;
                }

                // Assigned rather than coalesced. The applier builds this primitive from the exit
                // as it now stands (§4.15), so a null here is a condition that is genuinely gone -
                // coalescing would make a lock impossible to remove.
                entity.ToRoomKey = c.To;
                entity.RequiredFlagKey = c.RequiredFlagKey;
                entity.RequiredItemKey = c.RequiredItemKey;
                entity.RefusalMessage = c.RefusalMessage;
                return ContentAction.Update;
            }

            case RemoveExit c:
            {
                var entity = await db.RoomExits.FirstOrDefaultAsync(
                    e => e.FromRoomKey == c.From && e.Direction == c.Direction,
                    cancellationToken);

                if (entity is not null)
                {
                    db.RoomExits.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertAbility c:
            {
                var exists = await db.Abilities
                    .AsNoTracking()
                    .AnyAsync(a => a.Key == c.Key, cancellationToken);

                if (!exists)
                {
                    db.Abilities.Add(new Domain.Abilities.Ability
                    {
                        Key = c.Key,
                        Path = c.Path,
                        UnlockLevel = c.UnlockLevel,
                        Name = c.Name,
                        Description = c.Description,
                        CostType = c.CostType,
                        CostValue = c.CostValue,
                        CooldownPulses = c.CooldownPulses,
                        CooldownGroup = c.CooldownGroup,
                        CastTimePulses = c.CastTimePulses,
                        TargetingType = c.TargetingType,
                        Effects = [.. c.Effects.Select(e =>
                            new Domain.Abilities.AbilityEffectSpec(
                                e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))],
                    });

                    return ContentAction.Create;
                }

                // ExecuteUpdate rather than mutating a tracked entity, because Ability is
                // init-only on purpose: a content row is meant to be immutable, and opening every
                // property up so the writer can assign them would give the rest of the codebase a
                // mutable ability for the sake of one method. The other candidate - remove then
                // add - is a delete and an insert on the same primary key inside one
                // SaveChanges, where EF is free to order the insert first and collide.
                await db.Abilities
                    .Where(a => a.Key == c.Key)
                    .ExecuteUpdateAsync(
                        set => set
                            .SetProperty(a => a.Path, c.Path)
                            .SetProperty(a => a.UnlockLevel, c.UnlockLevel)
                            .SetProperty(a => a.Name, c.Name)
                            .SetProperty(a => a.Description, c.Description)
                            .SetProperty(a => a.CostType, c.CostType)
                            .SetProperty(a => a.CostValue, c.CostValue)
                            .SetProperty(a => a.CooldownPulses, c.CooldownPulses)
                            .SetProperty(a => a.CooldownGroup, c.CooldownGroup)
                            .SetProperty(a => a.CastTimePulses, c.CastTimePulses)
                            .SetProperty(a => a.TargetingType, c.TargetingType)
                            .SetProperty(
                                a => a.Effects,
                                c.Effects.ConvertAll(e => new Domain.Abilities.AbilityEffectSpec(
                                    e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))),
                        cancellationToken);

                return ContentAction.Update;
            }

            case DeleteAbility c:
            {
                var entity = await db.Abilities.FirstOrDefaultAsync(
                    a => a.Key == c.Key,
                    cancellationToken);

                if (entity is not null)
                {
                    db.Abilities.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertMobTemplate c:
            {
                var entity = await db.MobTemplates.FirstOrDefaultAsync(
                    m => m.Key == c.Key,
                    cancellationToken);

                if (entity is null)
                {
                    db.MobTemplates.Add(new Domain.Inhabitants.MobTemplate
                    {
                        Key = c.Key,
                        Name = c.Name,
                        Description = c.Description,
                        Icon = c.Icon,
                        Level = c.Level,
                        WanderIntervalPulses = c.WanderIntervalPulses,
                        BaseStats = c.BaseStats,
                        BaseXp = c.BaseXp,
                        BaseGold = c.BaseGold,
                        Behavior = c.Behavior,
                        Loot = c.Loot,
                        Attacks = c.Attacks,
                    });

                    return ContentAction.Create;
                }

                entity.Name = c.Name;
                entity.Description = c.Description;
                entity.Icon = c.Icon;
                entity.Level = c.Level;
                entity.WanderIntervalPulses = c.WanderIntervalPulses;
                entity.BaseStats = c.BaseStats;
                entity.BaseXp = c.BaseXp;
                entity.BaseGold = c.BaseGold;
                entity.Behavior = c.Behavior;
                entity.Loot = c.Loot;
                entity.Attacks = c.Attacks;
                return ContentAction.Update;
            }

            case DeleteMobTemplate c:
            {
                var entity = await db.MobTemplates.FirstOrDefaultAsync(
                    m => m.Key == c.Key,
                    cancellationToken);

                if (entity is not null)
                {
                    db.MobTemplates.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertItemTemplate c:
            {
                var entity = await db.ItemTemplates.FirstOrDefaultAsync(
                    i => i.Key == c.Key,
                    cancellationToken);

                if (entity is null)
                {
                    db.ItemTemplates.Add(new Domain.Items.ItemTemplate
                    {
                        Key = c.Key,
                        Name = c.Name,
                        Description = c.Description,
                        Icon = c.Icon,
                        Slots = [.. SlotRules.Normalize(c.Slots)],
                        IsTwoHanded = c.IsTwoHanded,
                        Weight = c.Weight,
                        BaseValue = c.BaseValue,
                        BaseStats = c.BaseStats,
                        AttackDelayPulses = c.AttackDelayPulses,
                        AttackVerb = c.AttackVerb,
                        IsQuestItem = c.IsQuestItem,
                        IsLore = c.IsLore,
                        IsNoDrop = c.IsNoDrop,
                        IsLightSource = c.IsLightSource,
                        FoodValue = c.FoodValue,
                        DrinkValue = c.DrinkValue,
                        Paths = [.. c.Paths],
                    });

                    return ContentAction.Create;
                }

                entity.Name = c.Name;
                entity.Description = c.Description;
                entity.Icon = c.Icon;
                entity.Slots = [.. SlotRules.Normalize(c.Slots)];
                entity.IsTwoHanded = c.IsTwoHanded;
                entity.Weight = c.Weight;
                entity.BaseValue = c.BaseValue;
                entity.BaseStats = c.BaseStats;
                entity.AttackDelayPulses = c.AttackDelayPulses;
                entity.AttackVerb = c.AttackVerb;
                entity.IsQuestItem = c.IsQuestItem;

                // These four were missing until the light source was added, and had been since the
                // `ItemRestrictions` migration: the API accepted them, the applier put them in the
                // running cache, the exporter wrote them and the importer read them - and nothing
                // ever wrote a row. So every lore, no-drop and Path restriction authored in the
                // builder or landed by an import survived exactly until the next restart, when the
                // cache was reloaded from a database that had never been told.
                entity.IsLore = c.IsLore;
                entity.IsNoDrop = c.IsNoDrop;
                entity.IsLightSource = c.IsLightSource;
                entity.FoodValue = c.FoodValue;
                entity.DrinkValue = c.DrinkValue;
                entity.Paths = [.. c.Paths];

                return ContentAction.Update;
            }

            case DeleteItemTemplate c:
            {
                var entity = await db.ItemTemplates.FirstOrDefaultAsync(
                    i => i.Key == c.Key,
                    cancellationToken);

                if (entity is not null)
                {
                    db.ItemTemplates.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertSpawner c:
            {
                var entity = await db.Spawners.FirstOrDefaultAsync(
                    s => s.Id == c.Id,
                    cancellationToken);

                if (entity is null)
                {
                    db.Spawners.Add(new Spawner
                    {
                        Id = c.Id,
                        ZoneKey = c.ZoneKey,
                        TemplateKey = c.TemplateKey,
                        TemplateKind = c.TemplateKind,
                        RoomKeys = new List<string>(c.RoomKeys),
                        TargetCount = c.TargetCount,
                        RespawnSeconds = c.RespawnSeconds,
                        Wanders = c.Wanders,
                        FightsAtLevel = c.FightsAtLevel,
                        NameModifier = c.NameModifier,
                    });

                    return ContentAction.Create;
                }

                // Update existing spawner (can't use init properties, so replace the object)
                db.Spawners.Remove(entity);
                db.Spawners.Add(new Spawner
                {
                    Id = c.Id,
                    ZoneKey = c.ZoneKey,
                    TemplateKey = c.TemplateKey,
                    TemplateKind = c.TemplateKind,
                    RoomKeys = new List<string>(c.RoomKeys),
                    TargetCount = c.TargetCount,
                    RespawnSeconds = c.RespawnSeconds,
                    Wanders = c.Wanders,
                    FightsAtLevel = c.FightsAtLevel,
                    NameModifier = c.NameModifier,
                });
                return ContentAction.Update;
            }

            case DeleteSpawner c:
            {
                var entity = await db.Spawners.FirstOrDefaultAsync(
                    s => s.Id == c.Id,
                    cancellationToken);

                if (entity is not null)
                {
                    db.Spawners.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertQuest c:
            {
                var entity = await db.Quests.FirstOrDefaultAsync(
                    q => q.Key == c.Key,
                    cancellationToken);

                if (entity is null)
                {
                    db.Quests.Add(new Quest
                    {
                        Key = c.Key,
                        // The endpoint refuses a create without a zone and falls back to the
                        // existing value on update, so this is never actually empty here.
                        ZoneKey = c.ZoneKey ?? string.Empty,
                        Name = c.Name,
                        Summary = c.Summary,
                        Description = c.Description,
                        GiverMobKey = c.GiverMobKey,
                        TurninMobKey = c.TurninMobKey,
                        RequiredItemKey = c.RequiredItemKey,
                        RequiredCount = c.RequiredCount,
                        RewardXp = c.RewardXp,
                        RewardGold = c.RewardGold,
                        RewardItemKey = c.RewardItemKey,
                        RewardItemCount = c.RewardItemCount,
                        RewardFlagKey = c.RewardFlagKey,
                        PrerequisiteQuestKeys = new List<string>(c.PrerequisiteQuestKeys),
                        IsRepeatable = c.IsRepeatable,
                        AutoStart = c.AutoStart,
                        Paths = [.. c.Paths],
                        Dialogue = new Dictionary<string, string>(c.Dialogue, StringComparer.Ordinal),
                        SortOrder = c.SortOrder,
                    });

                    return ContentAction.Create;
                }

                entity.Name = c.Name;
                entity.Summary = c.Summary;
                entity.Description = c.Description;
                entity.GiverMobKey = c.GiverMobKey;
                entity.TurninMobKey = c.TurninMobKey;
                entity.RequiredItemKey = c.RequiredItemKey;
                entity.RequiredCount = c.RequiredCount;
                entity.RewardXp = c.RewardXp;
                entity.RewardGold = c.RewardGold;
                entity.RewardItemKey = c.RewardItemKey;
                entity.RewardItemCount = c.RewardItemCount;
                entity.RewardFlagKey = c.RewardFlagKey;
                entity.PrerequisiteQuestKeys = new List<string>(c.PrerequisiteQuestKeys);
                entity.IsRepeatable = c.IsRepeatable;
                entity.AutoStart = c.AutoStart;
                entity.Paths = [.. c.Paths];
                entity.Dialogue = new Dictionary<string, string>(c.Dialogue, StringComparer.Ordinal);
                entity.SortOrder = c.SortOrder;
                return ContentAction.Update;
            }

            case DeleteQuest c:
            {
                var entity = await db.Quests.FirstOrDefaultAsync(
                    q => q.Key == c.Key,
                    cancellationToken);

                if (entity is not null)
                {
                    db.Quests.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case UpsertGameConfiguration c:
            {
                var entity = await db.GameConfigurations.FirstOrDefaultAsync(
                    g => g.Key == c.Key,
                    cancellationToken);

                var created = entity is null;

                if (entity is null)
                {
                    entity = new GameConfiguration { Key = c.Key, Name = c.Name };
                    db.GameConfigurations.Add(entity);
                }

                entity.Name = c.Name;
                entity.Description = c.Description;
                entity.StartingRoomKey = c.StartingRoomKey;
                entity.WelcomeMessage = c.WelcomeMessage;
                entity.BlockedWords = c.BlockedWords;
                entity.UpdatedAt = clock.GetUtcNow();

                // Null leaves the stored canon alone - see UpsertGameConfiguration.Canon.
                if (c.Canon is not null)
                {
                    entity.Canon = c.Canon;
                }

                if (c.WorldKeys is not null)
                {
                    entity.WorldKeys = new List<string>(c.WorldKeys);
                }

                // IsActive is untouched on purpose. An edit says what a configuration means, never
                // which one the server obeys - that only moves through ActivateGameConfiguration,
                // so an import can bring a configuration in without repointing a live server.
                return created ? ContentAction.Create : ContentAction.Update;
            }

            case DeleteGameConfiguration c:
            {
                var entity = await db.GameConfigurations.FirstOrDefaultAsync(
                    g => g.Key == c.Key,
                    cancellationToken);

                if (entity is not null)
                {
                    db.GameConfigurations.Remove(entity);
                }

                return ContentAction.Delete;
            }

            case ActivateGameConfiguration c:
            {
                // Cleared first, as its own statement, and this ordering is load-bearing. The
                // filtered unique index is checked per statement rather than per transaction, and
                // a partial index cannot be made deferrable - so handing EF an "unset the old" and
                // "set the new" pair in one batch lets it choose an order where two rows are
                // briefly live, and Postgres rejects the whole write. It failed exactly that way,
                // and surfaced as the second activation silently not taking.
                await db.GameConfigurations
                    .Where(g => g.IsActive && g.Key != c.Key)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(g => g.IsActive, false),
                        cancellationToken);

                var entity = await db.GameConfigurations.FirstOrDefaultAsync(
                    g => g.Key == c.Key,
                    cancellationToken);

                if (entity is not null)
                {
                    entity.IsActive = true;
                    entity.UpdatedAt = clock.GetUtcNow();
                }

                return ContentAction.Update;
            }

            default:
                // Every primitive the loop can produce needs an arm above. Quests reached
                // production without one, so a builder's quest save applied to memory, threw
                // here, and was rolled back by a full world reload - reported as a 500 with no
                // hint that the primitive was simply unhandled. Add the arm with the primitive.
                throw new InvalidOperationException(
                    $"{change.GetType().Name} is not a persistable primitive.");
        }
    }

    // -----------------------------------------------------------------------
    // Audit snapshots
    // -----------------------------------------------------------------------

    /// <summary>
    /// The entity as it currently stands in the database, or null when it does not exist -
    /// which is what makes "before" null on a create and "after" null on a delete.
    /// </summary>
    private async Task<string?> CaptureAsync(WorldChange change, CancellationToken cancellationToken)
    {
        switch (change)
        {
            case UpsertAbility or DeleteAbility:
            {
                var key = change.EntityKey;
                var entity = await db.Abilities.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key,
                    ["path"] = entity.Path.ToString(),
                    ["unlockLevel"] = entity.UnlockLevel,
                    ["name"] = entity.Name,
                    ["costType"] = entity.CostType.ToString(),
                    ["costValue"] = entity.CostValue,
                    ["cooldownPulses"] = entity.CooldownPulses,
                    ["castTimePulses"] = entity.CastTimePulses,
                    ["targetingType"] = entity.TargetingType.ToString(),
                    ["effects"] = JsonSerializer.SerializeToNode(entity.Effects),
                }.ToJsonString();
            }

            case UpsertWorld or DeleteWorld:
            {
                var key = change.EntityKey;
                var entity = await db.Worlds.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key,
                    ["name"] = entity.Name,
                    ["description"] = entity.Description,
                    ["sortOrder"] = entity.SortOrder,
                    ["flags"] = JsonNode.Parse(FlagSetJson.Serialize(entity.Flags)),
                }.ToJsonString();
            }

            case UpsertZone or DeleteZone:
            {
                var key = change.EntityKey;
                var entity = await db.Zones.AsNoTracking()
                    .FirstOrDefaultAsync(z => z.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key,
                    ["worldKey"] = entity.WorldKey,
                    ["name"] = entity.Name,
                    ["description"] = entity.Description,
                    ["minLevel"] = entity.MinLevel,
                    ["maxLevel"] = entity.MaxLevel,
                    ["flags"] = JsonNode.Parse(FlagSetJson.Serialize(entity.Flags)),
                }.ToJsonString();
            }

            case UpsertRoom or DeleteRoom:
            {
                if (!RoomKey.TryParse(change.EntityKey, out var key))
                {
                    return null;
                }

                var entity = await db.Rooms.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key.ToString(),
                    ["zoneKey"] = entity.ZoneKey,
                    ["title"] = entity.Title,
                    ["description"] = entity.Description,
                    ["flags"] = JsonNode.Parse(FlagSetJson.Serialize(entity.Flags)),
                    ["grid"] = new JsonArray([.. entity.Grid.Select(row => (JsonNode?)row)]),
                    ["legend"] = ToJson(entity.Legend),
                    ["editorX"] = entity.EditorX,
                    ["editorY"] = entity.EditorY,
                }.ToJsonString();
            }

            case SetExit or RemoveExit:
            {
                var (from, direction) = change switch
                {
                    SetExit c => (c.From, c.Direction),
                    RemoveExit c => (c.From, c.Direction),
                    _ => default,
                };

                var entity = await db.RoomExits.AsNoTracking()
                    .FirstOrDefaultAsync(
                        e => e.FromRoomKey == from && e.Direction == direction,
                        cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["from"] = entity.FromRoomKey.ToString(),
                    ["direction"] = entity.Direction.ToLowerName(),
                    ["to"] = entity.ToRoomKey.ToString(),

                    // Who may pass is as much a content decision as where the door goes, and an
                    // audit that recorded only the destination would leave "when did this gate
                    // stop being locked?" unanswerable (§4.15).
                    ["requiredFlagKey"] = entity.RequiredFlagKey,
                    ["requiredItemKey"] = entity.RequiredItemKey,
                    ["refusalMessage"] = entity.RefusalMessage,
                }.ToJsonString();
            }

            case UpsertItemTemplate or DeleteItemTemplate:
            {
                var key = change.EntityKey;
                var entity = await db.ItemTemplates.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key,
                    ["name"] = entity.Name,
                    ["description"] = entity.Description,
                    ["icon"] = entity.Icon,
                    ["slots"] = new JsonArray(
                        [.. entity.Slots.Select(sl => (JsonNode)JsonValue.Create(sl.ToString()))]),
                    ["twoHanded"] = entity.IsTwoHanded,
                    ["weight"] = entity.Weight,
                    ["baseValue"] = entity.BaseValue,
                    ["baseStats"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(entity.BaseStats)),
                    ["attackDelayPulses"] = entity.AttackDelayPulses,
                    ["attackVerb"] = entity.AttackVerb,
                }.ToJsonString();
            }

            case UpsertQuest or DeleteQuest:
            {
                var key = change.EntityKey;
                var entity = await db.Quests.AsNoTracking()
                    .FirstOrDefaultAsync(q => q.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key,
                    ["zoneKey"] = entity.ZoneKey,
                    ["name"] = entity.Name,
                    ["summary"] = entity.Summary,
                    ["description"] = entity.Description,
                    ["giverMobKey"] = entity.GiverMobKey,
                    ["turninMobKey"] = entity.TurninMobKey,
                    ["requiredItemKey"] = entity.RequiredItemKey,
                    ["requiredCount"] = entity.RequiredCount,
                    ["rewardXp"] = entity.RewardXp,
                    ["rewardGold"] = entity.RewardGold,
                    ["rewardItemKey"] = entity.RewardItemKey,
                    ["rewardItemCount"] = entity.RewardItemCount,
                    ["rewardFlagKey"] = entity.RewardFlagKey,
                    ["prerequisiteQuestKeys"] =
                        new JsonArray([.. entity.PrerequisiteQuestKeys.Select(k => (JsonNode?)k)]),
                    ["isRepeatable"] = entity.IsRepeatable,
                    ["autoStart"] = entity.AutoStart,
                    ["dialogue"] = ToJson(entity.Dialogue),
                    ["sortOrder"] = entity.SortOrder,
                }.ToJsonString();
            }

            // Worth an arm rather than the null default below: "who moved the starting room, and
            // what was it before?" is exactly the question the content audit exists to answer, and
            // this is the one setting whose being wrong strands every new character at once.
            case UpsertGameConfiguration or DeleteGameConfiguration or ActivateGameConfiguration:
            {
                var key = change.EntityKey;
                var entity = await db.GameConfigurations.AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Key == key, cancellationToken);

                return entity is null ? null : new JsonObject
                {
                    ["key"] = entity.Key,
                    ["name"] = entity.Name,
                    ["description"] = entity.Description,
                    ["startingRoomKey"] = entity.StartingRoomKey,
                    ["welcomeMessage"] = entity.WelcomeMessage,
                    ["blockedWords"] = entity.BlockedWords,
                    ["canon"] = entity.Canon,
                    ["worldKeys"] = new JsonArray([.. entity.WorldKeys.Select(k => (JsonNode?)k)]),

                    // Included here though it never travels in a bundle: an activation's whole
                    // content is this field moving, and an audit pair that showed no difference
                    // would be a record of nothing having happened.
                    ["isActive"] = entity.IsActive,
                }.ToJsonString();
            }

            default:
                return null;
        }
    }

    private static JsonObject ToJson(IReadOnlyDictionary<string, string> map)
    {
        var node = new JsonObject();

        foreach (var (key, value) in map)
        {
            node[key] = value;
        }

        return node;
    }
}

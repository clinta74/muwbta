using System.Threading.Channels;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Worlds;
using Muwbta.Domain.Items;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Commands;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Mutations;
using Muwbta.Domain.Randomness;
using Muwbta.Engine.Presentation;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Quests;
using Muwbta.Domain.Quests;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Time;
using Muwbta.Engine.World;
using EngineCombatSystem = Muwbta.Engine.Systems.CombatSystem;

namespace Muwbta.Engine.Tests.Infrastructure;

/// <summary>
/// Captures what would have been persisted, so a test can assert the primitives the loop
/// produced without a database anywhere near it.
/// </summary>
internal sealed class RecordingWriteQueue : IWorldWriteQueue
{
    public List<WorldWriteJob> Jobs { get; } = [];

    public IEnumerable<WorldChange> AllChanges => Jobs.SelectMany(j => j.Changes);

    public void Enqueue(WorldWriteJob job) => Jobs.Add(job);
}

/// <summary>
/// Captures admin requests instead of touching an account store, which the loop could not do
/// anyway (PLAN.md §7.7). What the command produced is the whole of what these tests assert.
/// </summary>
internal sealed class RecordingAdminQueue : IAccountAdminQueue
{
    public List<AccountAdminRequest> Requests { get; } = [];

    public void Enqueue(AccountAdminRequest request) => Requests.Add(request);
}

/// <summary>
/// Captures item persistence instead of touching a database. Removing an item has to reach
/// storage as well as the in-memory world, and only this records whether it did.
/// </summary>
internal sealed class RecordingItemSaveQueue : IItemSaveQueue
{
    public List<ItemInstance> Saved { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public void Enqueue(ItemInstance item) => Saved.Add(item);

    public void EnqueueDelete(Guid itemId) => Deleted.Add(itemId);

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Captures quest progress instead of writing it, so a test can assert what was saved.</summary>
internal sealed class RecordingQuestSaveQueue : ICharacterQuestSaveQueue
{
    public List<CharacterQuestSnapshot> Saved { get; } = [];

    /// <summary>Abandoned quests, as (character, quest key) pairs.</summary>
    public List<(Guid CharacterId, string QuestKey)> Deleted { get; } = [];

    public void Enqueue(CharacterQuestSnapshot snapshot) => Saved.Add(snapshot);

    public void EnqueueDelete(Guid characterId, string questKey) =>
        Deleted.Add((characterId, questKey));

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// A random source whose probability rolls are decided by the test.
/// </summary>
/// <remarks>
/// Dice still come from a seeded source, so damage stays realistic; only
/// <see cref="IRandomSource.NextDouble"/> is pinned. That is what <c>Chance</c> reads, which is
/// how a test says "this parry fires" or "this one does not" without inferring it from a seed.
/// </remarks>
internal sealed class ScriptedChanceSource(double nextDouble, int seed = 42) : IRandomSource
{
    private readonly SeededRandomSource _dice = new(seed);

    /// <summary>Never rolls a chance: any probability below 1.0 fails.</summary>
    public static ScriptedChanceSource Never => new(0.999);

    /// <summary>
    /// Lands the swing but turns nothing aside.
    /// </summary>
    /// <remarks>
    /// Above the largest parry chance in the game - a Warden's 0.20 - and below any hit chance a
    /// fight worth testing produces. <see cref="Never"/> cannot be used to mean "does not parry"
    /// any more: landing a blow is a probability now (PLAN.md §4.6) and reads the same
    /// <see cref="IRandomSource.NextDouble"/> the parry does, so refusing every chance refuses the
    /// swing itself and the blow that was supposed to get through never happens.
    /// </remarks>
    public static ScriptedChanceSource LandsUnparried => new(0.3);

    /// <summary>Always rolls a chance: any probability above 0 succeeds.</summary>
    public static ScriptedChanceSource Always => new(0.0);

    public int Next(int minInclusive, int maxExclusive) => _dice.Next(minInclusive, maxExclusive);

    public double NextDouble() => nextDouble;
}

/// <summary>
/// Wires the real WorldState, CommandRegistry, and PlayerView without the hosted service, so
/// command behaviour can be asserted synchronously. No timers, no sleeping, no database.
/// </summary>
internal sealed class WorldHarness
{
    private readonly Dictionary<Guid, Channel<OutboundEvent>> _channels = [];

    /// <param name="random">
    /// Overrides the seeded default, for tests that need a specific probability roll rather than
    /// whatever seed 42 happens to produce.
    /// </param>
    public WorldHarness(IRandomSource? random = null)
    {
        random ??= new SeededRandomSource(42);
        World = new WorldState(random);
        // AbilityCache was left null here, so every `cast` answered "not configured" and the
        // whole command path went untested - the buff tests reach past it and apply effects to
        // the world directly. Populated from the catalogue by DefineAbility.
        Commands = new CommandRegistry(abilityCache: AbilityCache, clock: Clock);
        // With the item cache, because the view reads it to decide whether a dark room can be
        // seen. Built without it, every dark room stays dark whatever anybody is carrying.
        View = new PlayerView(new RoomLayoutService(), ItemTemplates);
        Options = new EngineOptions { StartingRoom = RoomKey.Parse("test.zone.west") };
        // With the caches and the item queue the host wires up, because an edit that has to reach
        // them is one this harness must be able to see. Built bare, it applied a rename that
        // repointed exits and silently skipped everything else the loop's applier would have
        // touched, and no test could tell the difference.
        Applier = new WorldMutationApplier(
            World, View, Options, Quests, MobTemplates, ItemTemplates, Spawners, ItemSaves,
            // The real one, because "Respawn zone" places mobs through it (§7.5) and an applier
            // built without it can only refuse - which is a test asserting the refusal path.
            mobSpawner: MobSpawner);
        Writes = new RecordingWriteQueue();
        Editor = new LoopWorldEditor(Applier, Writes);
        Admin = new RecordingAdminQueue();
        // With the real effect registry, so a mob attack that carries an effect actually applies
        // it rather than swinging for damage alone.
        Combat = new EngineCombatSystem(
            Options,
            View,
            ItemTemplates,
            MobTemplates,
            // A real one. This was null, so RollLoot returned on its first line and no Engine test
            // could ever see a mob drop anything - the same shape as the AbilityCache that was left
            // null and made every `cast` test pass without reaching the cast path.
            itemSpawner: new Muwbta.Engine.Spawning.ItemSpawner(),
            logger: null,
            effects: new Domain.Abilities.Effects.EffectRegistry(),
            // The same cache `cast` resolves against, so a kill that levels somebody announces
            // what they learned rather than levelling them in silence.
            abilities: AbilityCache,
            // The test's own clock, so a loot claim can be watched expiring rather than waited out.
            clock: Clock);

        // With a cache and the real effect registry, so a cast resolves into an actual effect
        // rather than falling out of Tick with nothing to apply. The mob templates are what tell
        // an area effect which mobs are non-combatants; without them a Firestorm would kill the
        // shopkeeper and the test would pass.
        Abilities = new AbilitySystem(
            Clock,
            AbilityCache,
            new Domain.Abilities.Effects.EffectRegistry(),
            mobTemplates: MobTemplates);

        Shutdown = new ShutdownSchedule(Clock, ShutdownSignal);
    }

    public WorldState World { get; }

    /// <summary>Time under the test's control, so per-attack timing is exact rather than raced.</summary>
    public ManualGameClock Clock { get; } = new();

    /// <summary>Records that the world was asked to close, instead of closing the test run.</summary>
    internal sealed class RecordingShutdownSignal : IShutdownSignal
    {
        public int Stops { get; private set; }

        public bool Stopped => Stops > 0;

        public void Stop() => Stops++;
    }

    /// <summary>What <c>shutdown</c> would have done to the host.</summary>
    public RecordingShutdownSignal ShutdownSignal { get; } = new();

    /// <summary>The countdown the admin verb schedules. Ticked by <see cref="Pump"/>.</summary>
    public ShutdownSchedule Shutdown { get; private set; } = null!;

    /// <summary>Mob templates combat reads attack lists from.</summary>
    public MobTemplateCache MobTemplates { get; } = new();

    public EngineCombatSystem Combat { get; }

    public AbilitySystem Abilities { get; }

    /// <summary>The abilities `cast` can find. Populated by <see cref="DefineAbility"/>.</summary>
    public AbilityCache AbilityCache { get; } = new();

    /// <summary>
    /// Puts a real shipped ability into the cache, so a test casts the same thing the game does
    /// rather than a hand-built stand-in with convenient numbers.
    /// </summary>
    /// <remarks>
    /// Read from <c>shipped/abilities.json</c>, which is where the set lives — it used to come
    /// from <c>AbilityCatalogue</c>, which is four examples now. The intent is unchanged and is
    /// the whole value of these tests: a cast in here spends the same cost, waits the same
    /// cooldown, and applies the same effect parameters as a cast in the game.
    /// </remarks>
    public Domain.Abilities.Ability DefineAbility(string key)
    {
        var ability = ShippedAbilities.Get(key);
        AbilityCache.Put(ability);
        return ability;
    }

    /// <summary>
    /// Advances time the way <c>GameLoop.Pulse</c> does: one pulse at a time, abilities before
    /// combat. Stepping in single pulses matters - batching would quantise attack timing in the
    /// harness rather than in the game, and hide exactly the bugs these tests exist to catch.
    /// </summary>
    public void Pump(int pulses = 1)
    {
        for (var i = 0; i < pulses; i++)
        {
            Abilities.Tick(World);
            Combat.Tick(World, Clock.CurrentPulse);

            // After combat and every pulse, exactly as GameLoop runs it. Left out, the harness was
            // a world where effects never ended - so no test could see who an expiry was narrated
            // to, which is how a Warden came to be told about a debuff on somebody else's corpse.
            EffectExpirySystem.Tick(World, Clock.CurrentPulse);

            // Last in the pulse, matching GameLoop: the countdown announces into a world that has
            // finished being updated.
            Shutdown.Tick(World);
            Clock.AdvancePulses(1);
        }
    }

    /// <summary>Item templates the registry resolves slots and descriptions from.</summary>
    public ItemTemplateCache ItemTemplates { get; } = new();

    /// <summary>The spawner rules a builder edit has to keep current.</summary>
    public SpawnerCache Spawners { get; } = new();

    /// <summary>Places mobs from templates, the way the sweep and a zone respawn both do.</summary>
    public MobSpawner MobSpawner { get; } = new();

    /// <summary>
    /// Quests the command layer reads. Populated by <see cref="DefineQuest"/>.
    /// </summary>
    /// <remarks>
    /// This used to be left null, so <c>Talk</c> and <c>TryTurnInQuest</c> returned on their
    /// first line and every quest test passed without reaching the code it named (PLAN §12).
    /// </remarks>
    public QuestCache Quests { get; } = new();

    /// <summary>Quest progress the commands handed off to be persisted.</summary>
    public RecordingQuestSaveQueue QuestSaves { get; } = new();

    private readonly FakeItemTemplateRepository _itemTemplateRepo = new();

    public CommandRegistry Commands { get; }

    public PlayerView View { get; }

    public EngineOptions Options { get; }

    public WorldMutationApplier Applier { get; }

    public RecordingWriteQueue Writes { get; }

    public LoopWorldEditor Editor { get; }

    public RecordingAdminQueue Admin { get; }

    /// <summary>What the commands under test handed off to be persisted or deleted.</summary>
    public RecordingItemSaveQueue ItemSaves { get; } = new();

    /// <summary>Applies a builder edit exactly as the loop would, minus persistence.</summary>
    public MutationResult Mutate(WorldChange change) => Applier.Apply(change);

    /// <summary>
    /// Three rooms west-to-east, plus a fourth exit off the east room that points at a room
    /// which does not exist - the dangling-exit case live editing makes routine.
    /// </summary>
    public static (Room West, Room Middle, Room East) BuildTestRooms()
    {
        var west = NewRoom(
            "west",
            grid: ["#######", "#.....#", "#.....#", "#######"],
            legend: new() { ["#"] = "wall", ["."] = "floor" });
        var middle = NewRoom("middle");
        var east = NewRoom("east");

        Link(west, Direction.East, middle);
        Link(middle, Direction.East, east);

        east.Exits.Add(new RoomExit
        {
            FromRoomKey = east.Key,
            Direction = Direction.North,
            ToRoomKey = RoomKey.Parse("test.zone.nowhere"),
        });

        return (west, middle, east);
    }

    /// <summary>
    /// The world plus its containing zone and world rows, which the flag chain needs: a room
    /// whose zone is not loaded can only ever resolve to the registry default.
    /// </summary>
    public void LoadTestWorld()
    {
        var (west, middle, east) = BuildTestRooms();

        World.Load(
            [new Domain.Worlds.World { Key = "test", Name = "Test" }],
            [new Zone { Key = "test.zone", WorldKey = "test", Name = "Test Zone" }],
            [west, middle, east]);
    }

    public Zone Zone => World.FindZone("test.zone")!;

    public Domain.Worlds.World World_ => World.FindWorld("test")!;

    public static Room NewRoom(
        string slug,
        string[]? grid = null,
        Dictionary<string, string>? legend = null) =>
        new()
        {
            Key = RoomKey.Create("test", "zone", slug),
            ZoneKey = "test.zone",
            Title = $"The {slug} room",
            Description = $"A featureless {slug} room used for testing.",
            Grid = [.. grid ?? []],
            Legend = legend ?? [],
        };

    public static void Link(Room from, Direction direction, Room to)
    {
        from.Exits.Add(new RoomExit
        {
            FromRoomKey = from.Key,
            Direction = direction,
            ToRoomKey = to.Key,
        });

        to.Exits.Add(new RoomExit
        {
            FromRoomKey = to.Key,
            Direction = direction.Opposite(),
            ToRoomKey = from.Key,
        });
    }

    public PlayerActor AddPlayer(
        string name,
        RoomKey at,
        AccountRole role = AccountRole.Player,
        CharacterPath path = CharacterPath.Warden,
        int level = 1)
    {
        var channel = Channel.CreateUnbounded<OutboundEvent>();

        var character = NewCharacter(name, at, path);
        character.Level = level;

        var actor = new PlayerActor
        {
            Character = character,
            Role = role,
            SessionId = Guid.CreateVersion7(),
            Output = channel.Writer,
        };

        _channels[actor.CharacterId] = channel;
        World.Add(actor);
        return actor;
    }

    public static Character NewCharacter(
        string name,
        RoomKey at,
        CharacterPath path = CharacterPath.Warden) => new()
    {
        AccountId = Guid.CreateVersion7(),
        Name = name,
        Path = path,
        Attributes = AttributeSet.Baseline,
        Vitals = Vitals.StartingFor(path),
        RoomKey = at,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Runs a command exactly as the game loop would, minus the loop.</summary>
    public CommandContext Execute(PlayerActor actor, string input)
    {
        var (verb, argument) = CommandRegistry.Split(input);
        var definition = Commands.Find(verb);

        if (definition is null)
        {
            // Mirrors GameLoop.HandleCommand: a verb the table does not have may still be one of
            // this character's abilities, since skills are verbs. Without this the harness would
            // dispatch differently from the game, and a test could pass against a path the loop
            // never takes.
            if (Commands.FindAbilityVerb(actor.Character, verb, argument) is { } ability)
            {
                definition = ability.Definition;
                argument = ability.Argument;
            }
            else
            {
                throw new InvalidOperationException($"No command matched '{verb}'.");
            }
        }

        var context = new CommandContext
        {
            Actor = actor,
            World = World,
            View = View,
            Editor = Editor,
            AdminQueue = Admin,
            ItemSaveQueue = ItemSaves,
            ItemTemplates = ItemTemplates,
            MobTemplates = MobTemplates,
            Options = Options,
            Shutdown = Shutdown,
            Clock = Clock,
            Quests = Quests,
            QuestSaveQueue = QuestSaves,
            Verb = verb,
            Argument = argument,
        };

        definition.Handler(context);

        // The loop flushes marked rooms at the end of every pulse, so the harness does it at the
        // end of every command - otherwise a handler that marks a room would leave its map and
        // contents frames un-sent, and a test asserting on them would be asserting against a
        // dispatch path the game never takes. Mirrors GameLoop.Pulse, for the same reason the
        // ability-verb fallback above mirrors GameLoop.HandleCommand.
        View.FlushChangedRooms(World);

        return context;
    }

    /// <summary>
    /// Puts a free-form bag through the same JSON round trip Postgres and the builder API put it
    /// through, so its values arrive as <c>JsonElement</c> rather than as the C# types they were
    /// written as.
    /// </summary>
    /// <remarks>
    /// A bag built inline in a test is the one shape the running game never sees. Shops and mob
    /// emotes were both dead in production while their hand-built test doubles passed, because
    /// the code pattern-matched <c>is bool</c> and <c>is List&lt;object&gt;</c>. Any test that
    /// asserts on behavior or item state should author it through here.
    /// </remarks>
    public static Dictionary<string, object> AsPersisted(Dictionary<string, object> bag)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(bag);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
    }

    /// <summary>
    /// Registers a quest in the cache the command layer reads, and returns it.
    /// </summary>
    /// <remarks>
    /// Defaults to the harness's own zone, because a quest whose zone is not loaded cannot
    /// resolve its rewards through any multiplier and would quietly pay its authored numbers.
    /// </remarks>
    /// <summary>
    /// Takes a quest on the way a player now does: hear the offer, then answer with its name.
    /// </summary>
    /// <remarks>
    /// <c>talk &lt;giver&gt;</c> stopped starting quests by itself (PLAN.md §4.9), so a test that
    /// wants a quest Active has to say yes. The <em>key</em> is used as the answer rather than a
    /// keyword, because it is an exact match at rank 1 and cannot be made ambiguous by a quest
    /// added to the same giver later.
    ///
    /// The offer itself is sent first, even though answering does not require it, so the sequence
    /// under test is the one a player actually types.
    /// </remarks>
    public void TakeQuest(PlayerActor actor, string giver, string questKey)
    {
        Execute(actor, $"talk {giver}");
        Execute(actor, $"talk {giver} {questKey}");
    }

    public Quest DefineQuest(
        string key,
        string giverMobKey,
        string? turninMobKey = null,
        string? requiredItemKey = null,
        int requiredCount = 1,
        int rewardXp = 0,
        int rewardGold = 0,
        string? rewardItemKey = null,
        int rewardItemCount = 1,
        bool repeatable = false,
        string zoneKey = "test.zone",
        Dictionary<string, string>? dialogue = null,
        string? rewardFlagKey = null)
    {
        var quest = new Quest
        {
            Key = key,
            Name = key,
            ZoneKey = zoneKey,
            GiverMobKey = giverMobKey,
            TurninMobKey = turninMobKey ?? giverMobKey,
            RequiredItemKey = requiredItemKey,
            RequiredCount = requiredCount,
            RewardXp = rewardXp,
            RewardGold = rewardGold,
            RewardItemKey = rewardItemKey,
            RewardItemCount = rewardItemCount,
            RewardFlagKey = rewardFlagKey,
            IsRepeatable = repeatable,
            Dialogue = dialogue ?? [],
        };

        Quests.Put(quest);
        return quest;
    }

    /// <summary>Sets this zone's difficulty dial, so reward and spawn resolution have something to scale by.</summary>
    public void SetZoneMultipliers(Action<Multipliers> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Zone.Multipliers);
    }

    /// <summary>Registers an item template (so its slot and description resolve) and returns it.</summary>
    public ItemTemplate DefineItem(
        string key,
        string name,
        ItemSlot? slot,
        string description = "",
        int value = 0,
        int weight = 0,
        int? foodValue = null,
        int? drinkValue = null)
    {
        var template = new ItemTemplate
        {
            Key = key,
            Name = name,
            Icon = "$",
            Slots = slot is null ? [] : [slot.Value],
            Description = description,
            BaseValue = value,
            Weight = weight,
            FoodValue = foodValue,
            DrinkValue = drinkValue,
        };

        _itemTemplateRepo.Add(template);
        ItemTemplates.LoadAsync(_itemTemplateRepo, CancellationToken.None).GetAwaiter().GetResult();
        return template;
    }

    /// <summary>
    /// Registers a weapon template with a speed and a verb. A null delay is the "declares no
    /// speed" case: a default-speed main hand, and an off-hand item that never strikes.
    /// </summary>
    /// <param name="attackBonus">
    /// Defaults absurdly high so a timing test never has to care whether a swing connected -
    /// the question under test is which pulse it happened on.
    /// </param>
    public ItemTemplate DefineWeapon(
        string key,
        string name,
        ItemSlot slot,
        int? delayPulses,
        string? verb = null,
        int damageMin = 1,
        int damageMax = 1,
        int attackBonus = 100) =>
        DefineWeapon(key, name, [slot], delayPulses, verb, damageMin, damageMax, attackBonus);

    /// <summary>
    /// The same, for a weapon that fits more than one slot - or that claims both hands.
    /// </summary>
    public ItemTemplate DefineWeapon(
        string key,
        string name,
        IReadOnlyList<ItemSlot> slots,
        int? delayPulses,
        string? verb = null,
        int damageMin = 1,
        int damageMax = 1,
        int attackBonus = 100,
        bool twoHanded = false)
    {
        var template = new ItemTemplate
        {
            Key = key,
            Name = name,
            Icon = "/",
            Slots = [.. SlotRules.Normalize(slots)],
            IsTwoHanded = twoHanded,
            AttackDelayPulses = delayPulses,
            AttackVerb = verb,
            BaseStats = new Dictionary<string, object>
            {
                ["damageMin"] = damageMin,
                ["damageMax"] = damageMax,
                ["bonus"] = attackBonus,
            },
        };

        _itemTemplateRepo.Add(template);
        ItemTemplates.LoadAsync(_itemTemplateRepo, CancellationToken.None).GetAwaiter().GetResult();
        return template;
    }

    /// <summary>
    /// Registers a template the caller built itself, for the fields the convenience helpers above
    /// do not take - the three restrictions among them.
    /// </summary>
    public ItemTemplate AddItemTemplate(ItemTemplate template)
    {
        _itemTemplateRepo.Add(template);
        ItemTemplates.LoadAsync(_itemTemplateRepo, CancellationToken.None).GetAwaiter().GetResult();
        return template;
    }

    /// <summary>Puts a loose instance of a template on the floor of a room, ready to be taken.</summary>
    public ItemInstance DropItemInRoom(ItemTemplate template, RoomKey at)
    {
        // RoomKey before AddItem, and AddItem alone. WorldState.AddItem ignores an instance whose
        // RoomKey is still null, so registering first and dropping afterwards puts the item in the
        // room's list and never in the world's index - which reads as a room you can see an item in
        // and cannot pick it up from.
        var item = new ItemInstance
        {
            TemplateKey = template.Key,
            TemplateName = template.Name,
            ResolvedStats = new Dictionary<string, object>(template.BaseStats),
            Value = template.BaseValue,
            RoomKey = at.ToString(),
        };

        World.AddItem(item);
        return item;
    }

    /// <summary>Puts an instance of a template into a character's inventory and returns it.</summary>
    public ItemInstance GiveItem(
        PlayerActor actor,
        ItemTemplate template,
        Dictionary<string, object>? state = null)
    {
        var item = new ItemInstance
        {
            TemplateKey = template.Key,
            TemplateName = template.Name,
            OwnerCharacterId = actor.CharacterId,
            ResolvedStats = new Dictionary<string, object>(template.BaseStats),
            Value = template.BaseValue,
            State = state ?? [],
        };

        World.LoadCharacterItems(actor.CharacterId, [.. World.InventoryOf(actor.CharacterId), item]);
        return item;
    }

    /// <summary>Gives the character an instance of the template already equipped in that slot.</summary>
    public ItemInstance Equip(PlayerActor actor, ItemTemplate template, ItemSlot slot)
    {
        var item = GiveItem(actor, template);
        item.EquippedSlot = slot;
        return item;
    }

    /// <summary>
    /// Registers a mob template and puts one instance of it in a room, ready to fight.
    /// </summary>
    /// <param name="level">
    /// Matters to any test that asserts an experience award (§5.3): a mob below half the killer's
    /// level pays nothing at all. The default of 1 stays, because most tests here are about combat
    /// rather than reward and a level 1 mob is the simplest thing to fight - but a test that checks
    /// experience must set this, or it is asserting the relevance rule by accident.
    /// </param>
    public Mob AddMob(
        string templateKey,
        RoomKey at,
        IEnumerable<MobAttack>? attacks = null,
        int health = 100,
        string name = "rat",
        int damageMin = 1,
        int damageMax = 1,
        Dictionary<string, object>? behavior = null,
        int level = 1,
        string icon = "r",
        IEnumerable<Dictionary<string, object>>? loot = null)
    {
        MobTemplates.Put(new MobTemplate
        {
            Key = templateKey,
            Name = name,
            Icon = icon,
            Level = level,
            Attacks = [.. attacks ?? []],
            Behavior = behavior ?? [],
            Loot = [.. loot ?? []],
        });

        // Resolved the way MobSpawner resolves it, so a test that sets a zone's band or its
        // multipliers before adding a mob gets the level the running game would give it (§4.7).
        // Snapshotted here for the same reason it is snapshotted there: retuning the zone
        // afterwards changes what spawns next, not what is already standing in the room.
        var zone = World.FindZone(at.ZoneKey);
        var owningWorld = zone is null ? null : World.FindWorld(zone.WorldKey);
        var effectiveLevel = zone is null || owningWorld is null
            ? level
            : MobLevel.Effective(level, owningWorld.Multipliers, zone.Multipliers, zone.MinLevel);

        var mob = new Mob
        {
            TemplateKey = templateKey,
            TemplateName = name,
            // Stamped from the template, exactly as MobSpawner does it. A harness that builds an
            // instance the spawner would not is how a field goes untested for a phase.
            Icon = icon,
            RoomKey = at.ToString(),
            Level = level,
            EffectiveLevel = effectiveLevel,
            Vitals = new Vitals
            {
                Health = health,
                HealthMax = health,
                Focus = 0,
                FocusMax = 0,
                Stamina = 0,
                StaminaMax = 0,
            },
            ResolvedStats = new Dictionary<string, object>
            {
                ["damageMin"] = damageMin,
                ["damageMax"] = damageMax,
                // As with DefineWeapon: connect every time, so the assertion is about timing.
                ["attackRating"] = 100,
            },
        };

        World.AddMob(mob);
        return mob;
    }

    private sealed class FakeItemTemplateRepository : IItemTemplateRepository
    {
        private readonly Dictionary<string, ItemTemplate> _templates = [];

        public void Add(ItemTemplate template) => _templates[template.Key] = template;

        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(_templates.GetValueOrDefault(key));

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>([.. _templates.Values]);
    }

    /// <summary>Everything queued for this player since the last drain.</summary>
    public List<OutboundEvent> Drain(PlayerActor actor)
    {
        var events = new List<OutboundEvent>();
        var reader = _channels[actor.CharacterId].Reader;

        while (reader.TryRead(out var gameEvent))
        {
            events.Add(gameEvent);
        }

        return events;
    }

    /// <summary>All text produced for this player, flattened into one string.</summary>
    public string DrainText(PlayerActor actor) =>
        string.Concat(Drain(actor)
            .Where(e => e.Type == EventTypes.Text)
            .Cast<OutboundEvent>()
            .Select(e => string.Concat(((TextPayload)e.Payload).Spans.Select(s => s.T))));

    /// <summary>
    /// The spans themselves, for the properties that live in a span rather than in its text.
    /// </summary>
    /// <remarks>
    /// <see cref="DrainText"/> concatenates <c>T</c> and throws the rest away, which is right for
    /// nearly every assertion and useless for the two fields that make a span do something: the
    /// builder path and the command.
    /// </remarks>
    public IReadOnlyList<TextSpan> DrainSpans(PlayerActor actor) =>
        [.. Drain(actor)
            .Where(e => e.Type == EventTypes.Text)
            .Cast<OutboundEvent>()
            .SelectMany(e => ((TextPayload)e.Payload).Spans)];
}

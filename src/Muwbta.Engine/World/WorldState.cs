using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Randomness;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Abilities;
using Muwbta.Engine.Social;

namespace Muwbta.Engine.World;

/// <summary>
/// The authoritative in-memory world. Owned by the single game-loop thread and deliberately
/// NOT thread-safe (PLAN.md §2.1) - adding a lock here would be a sign something is calling
/// it from the wrong place.
/// </summary>
public sealed class WorldState(IRandomSource random)
{
    private readonly IRandomSource _random = random;
    private readonly Dictionary<string, Domain.Worlds.World> _worlds = [];
    private readonly Dictionary<string, Zone> _zones = [];
    private readonly Dictionary<RoomKey, Room> _rooms = [];
    private readonly Dictionary<RoomKey, List<PlayerActor>> _occupants = [];
    private readonly Dictionary<Guid, PlayerActor> _bySession = [];
    private readonly Dictionary<Guid, PlayerActor> _byCharacter = [];
    private readonly Dictionary<Guid, Mob> _mobs = [];
    private readonly Dictionary<RoomKey, List<Mob>> _mobsByRoom = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _mobsBySpawner = [];
    private readonly Dictionary<Guid, ItemInstance> _items = [];
    private readonly Dictionary<RoomKey, List<ItemInstance>> _itemsByRoom = [];
    private readonly Dictionary<RoomKey, Combat> _combatsByRoom = [];
    private readonly CastQueueService _castQueue = new();
    private readonly Dictionary<(Guid CharacterId, string AbilityKey), long> _abilityCooldowns = [];

    /// <summary>When each sleeping character is next due a dream (<see cref="Systems.DreamSystem"/>).</summary>
    /// <remarks>
    /// Transient like the cooldowns above and for the same reason: it is a timer, not a fact about
    /// the character. Somebody who logs out asleep and back in has not been dreaming in the
    /// meantime, and a persisted due-time would fire the instant they returned.
    /// </remarks>
    private readonly Dictionary<Guid, long> _nextDreamPulse = [];
    private readonly Dictionary<Guid, List<ActiveEffect>> _activeEffects = [];
    private readonly Dictionary<Guid, Dictionary<string, CharacterQuest>> _questsByCharacter = [];
    private readonly PartyRegistry _parties = new();

    public IRandomSource Random => _random;

    /// <summary>
    /// Who is grouped with whom (PLAN.md §5.3). Lives here rather than in a table because a
    /// party is session-scoped: it describes the world as it stands, like combat and active
    /// effects do, and it ends with the session rather than outliving it.
    /// </summary>
    public PartyRegistry Parties => _parties;

    public CastQueueService CastQueue => _castQueue;

    /// <summary>Get the last pulse when an ability was cast (for cooldown checking).</summary>
    /// <summary>
    /// The pulse an ability was last cast on, or null if this character has never cast it.
    /// </summary>
    /// <remarks>
    /// Null rather than 0. Zero is a real pulse - the one the server starts on - so returning it
    /// for "never cast" made every ability read as freshly used at boot, and the whole spellbook
    /// was refused for its own cooldown duration after every restart. It also made a cast at
    /// pulse 0 untestable, which is why nothing had noticed.
    /// </remarks>
    public long? GetAbilityCooldown(Guid characterId, string abilityKey) =>
        _abilityCooldowns.TryGetValue((characterId, abilityKey), out var lastPulse)
            ? lastPulse
            : null;

    /// <summary>Set the last pulse when an ability was cast.</summary>
    public void SetAbilityCooldown(Guid characterId, string abilityKey, long pulse)
    {
        var key = (characterId, abilityKey);
        _abilityCooldowns[key] = pulse;
    }

    /// <summary>When this character is next due a dream, or null if they are not asleep.</summary>
    public long? NextDreamPulse(Guid characterId) =>
        _nextDreamPulse.TryGetValue(characterId, out var pulse) ? pulse : null;

    public void SetNextDreamPulse(Guid characterId, long pulse) =>
        _nextDreamPulse[characterId] = pulse;

    /// <summary>Forgets a character's dream timer, on waking or on leaving the world.</summary>
    public void ClearDreams(Guid characterId) => _nextDreamPulse.Remove(characterId);

    public int RoomCount => _rooms.Count;

    public int PlayerCount => _byCharacter.Count;

    public IEnumerable<PlayerActor> AllPlayers => _byCharacter.Values;

    public void Load(
        IEnumerable<Domain.Worlds.World> worlds,
        IEnumerable<Zone> zones,
        IEnumerable<Room> rooms)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(rooms);

        _worlds.Clear();
        _zones.Clear();
        _rooms.Clear();

        foreach (var world in worlds)
        {
            _worlds[world.Key] = world;
        }

        foreach (var zone in zones)
        {
            _zones[zone.Key] = zone;
        }

        foreach (var room in rooms)
        {
            _rooms[room.Key] = room;
        }
    }

    public bool TryGetRoom(RoomKey key, out Room room) => _rooms.TryGetValue(key, out room!);

    // -----------------------------------------------------------------------
    // Content mutation (PLAN.md §7.3). Called only from WorldMutationApplier,
    // which the game loop calls on its own thread.
    // -----------------------------------------------------------------------

    public void PutWorld(Domain.Worlds.World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _worlds[world.Key] = world;
    }

    public bool RemoveWorld(string key) => _worlds.Remove(key);

    public void PutZone(Zone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zones[zone.Key] = zone;
    }

    public bool RemoveZone(string key) => _zones.Remove(key);

    public void PutRoom(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);
        _rooms[room.Key] = room;
    }

    /// <summary>
    /// Removes the room and the exits out of it. Exits pointing <em>at</em> it are left alone
    /// and become dangling links, which movement fails closed on (PLAN.md §7.4).
    /// </summary>
    public bool RemoveRoom(RoomKey key)
    {
        _occupants.Remove(key);
        return _rooms.Remove(key);
    }

    /// <summary>Every exit anywhere in the world that points at this room.</summary>
    public IEnumerable<RoomExit> ExitsPointingAt(RoomKey key) =>
        _rooms.Values.SelectMany(r => r.Exits).Where(e => e.ToRoomKey == key);

    public Room? FindRoom(RoomKey key) => _rooms.GetValueOrDefault(key);

    public Zone? FindZone(string zoneKey) => _zones.GetValueOrDefault(zoneKey);

    public Domain.Worlds.World? FindWorld(string worldKey) => _worlds.GetValueOrDefault(worldKey);

    /// <summary>
    /// The level a mob fights at (PLAN.md §4.7) — the snapshot taken at spawn, or its authored
    /// level for a mob that never went through the spawner.
    /// </summary>
    /// <remarks>
    /// <b>Reads the snapshot rather than recomputing.</b> <see cref="MobLevel.Effective"/> runs
    /// once, in <c>MobSpawner</c>, alongside every other multiplier resolution — so retuning a zone
    /// changes what spawns next rather than silently re-levelling everything already standing in
    /// it. Recomputing here would quietly undo that and make a mob's level depend on when you
    /// asked.
    ///
    /// <b>Lives here so that experience and <c>consider</c> cannot disagree.</b> They are the same
    /// judgement shown two ways — one as a reward and one as a warning — and a copy of this lookup
    /// in each caller would drift the first time either was touched, leaving <c>consider</c>
    /// confidently describing a fight the reward rules score differently.
    ///
    /// A mob built by hand — a test, or the builder's <c>spawn</c> before it reaches the spawner —
    /// has no snapshot, and its authored level is the honest answer rather than a reason to refuse
    /// a reward or to lie in an assessment.
    /// </remarks>
    public int EffectiveLevelOf(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        return mob.EffectiveLevel > 0 ? mob.EffectiveLevel : mob.Level;
    }

    public IEnumerable<Domain.Worlds.World> AllWorlds => _worlds.Values;

    public IEnumerable<Zone> AllZones => _zones.Values;

    public IEnumerable<Room> AllRooms => _rooms.Values;

    public IEnumerable<Zone> ZonesIn(string worldKey) =>
        _zones.Values.Where(z => string.Equals(z.WorldKey, worldKey, StringComparison.Ordinal));

    public IEnumerable<Room> RoomsIn(string zoneKey) =>
        _rooms.Values.Where(r => string.Equals(r.ZoneKey, zoneKey, StringComparison.Ordinal));

    /// <summary>
    /// Resolves a flag down room → zone → world → registry default (PLAN.md §4.10). The
    /// lookups are done here rather than through navigation properties because the world is
    /// loaded AsNoTracking, so <see cref="Room.Zone"/> is null in the running engine.
    /// </summary>
    public FlagResolution ResolveFlag(Room room, RoomFlag flag)
    {
        ArgumentNullException.ThrowIfNull(room);

        var zone = FindZone(room.ZoneKey);
        var world = zone is null ? null : FindWorld(zone.WorldKey);

        return RoomFlags.Resolve(flag, room.Flags, zone?.Flags, world?.Flags);
    }

    /// <summary>
    /// True when the flag resolves on. A room that does not exist resolves to the registry
    /// default, which is always the safe value - so a deleted room never becomes PvP.
    /// </summary>
    public bool IsFlagSet(RoomKey key, RoomFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var room = FindRoom(key);
        return room is null ? flag.DefaultBoolean : ResolveFlag(room, flag).Value;
    }

    /// <summary>
    /// A text flag resolved for this room, or the registry default when nothing declares it.
    /// </summary>
    public string TextFlag(RoomKey key, RoomFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var room = FindRoom(key);

        if (room is null)
        {
            return flag.DefaultText;
        }

        var zone = FindZone(room.ZoneKey);
        var world = zone is null ? null : FindWorld(zone.WorldKey);

        return RoomFlags.ResolveText(flag, room.Flags, zone?.Flags, world?.Flags).Value;
    }

    /// <summary>
    /// A text flag resolved for a whole world, for anything that is a property of the realm
    /// rather than of a room.
    /// </summary>
    /// <remarks>
    /// The weather asks this: a sky belongs to a world, so the room and zone levels of the chain
    /// have nothing to say about it even though a builder may set them. A zone that declares
    /// itself alpine changes what a <em>room</em> resolves, and changes nothing about the front
    /// crossing the realm - which is the honest answer while weather is per-world.
    /// </remarks>
    public string WorldTextFlag(string worldKey, RoomFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        return RoomFlags.ResolveText(flag, null, null, FindWorld(worldKey)?.Flags).Value;
    }

    public PlayerActor? FindBySession(Guid sessionId) => _bySession.GetValueOrDefault(sessionId);

    public PlayerActor? FindByCharacter(Guid characterId) =>
        _byCharacter.GetValueOrDefault(characterId);

    /// <summary>Case-insensitive, so "kael" finds Kael - matching how players actually type.</summary>
    public PlayerActor? FindPlayerByName(string name) =>
        _byCharacter.Values.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PlayerActor> OccupantsOf(RoomKey key) =>
        _occupants.TryGetValue(key, out var list) ? list : [];

    /// <summary>Everyone in the room except the given actor - the usual audience for a message.</summary>
    public IEnumerable<PlayerActor> OthersIn(RoomKey key, PlayerActor except) =>
        OccupantsOf(key).Where(p => p.CharacterId != except.CharacterId);

    /// <summary>
    /// Everyone in the room with their eyes open - the audience for anything that is only
    /// <em>seen</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Seen is filtered; heard is not.</b> A sleeping player is still told what is said to the
    /// room, still told they are being hit, and still gets their dreams - but somebody walking in,
    /// picking something up or wandering out is a thing you perceive by looking, and they are not
    /// looking. Sleep otherwise costs nothing and pays the best regen in the game, which made a
    /// sleeper the best-informed person in the room.
    /// </para>
    /// <para>
    /// This existed twice by hand before it existed once here - <c>emote</c> and the mob idle
    /// lines each wrote the skip themselves, and every other way a room announces something did
    /// not. One list, so the next one cannot be forgotten.
    /// </para>
    /// </remarks>
    public IEnumerable<PlayerActor> AwakeIn(RoomKey key) =>
        OccupantsOf(key).Where(p => p.Character.RestState != CharacterRestState.Sleep);

    /// <summary>Everyone awake in the room except the given actor.</summary>
    public IEnumerable<PlayerActor> OthersAwakeIn(RoomKey key, PlayerActor except) =>
        AwakeIn(key).Where(p => p.CharacterId != except.CharacterId);

    public void Add(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _byCharacter[actor.CharacterId] = actor;
        _bySession[actor.SessionId] = actor;
        OccupantList(actor.RoomKey).Add(actor);
    }

    /// <summary>
    /// Takes a character out of the world. The one door out, which is why the party registry is
    /// cleaned up here rather than at each of the four call sites that lead to it.
    /// </summary>
    public void Remove(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _byCharacter.Remove(actor.CharacterId);
        _bySession.Remove(actor.SessionId);
        OccupantList(actor.RoomKey).Remove(actor);
        _parties.Forget(actor.CharacterId);
    }

    /// <summary>Rebinds an actor to a new session after a reconnect.</summary>
    public void Rebind(PlayerActor actor, Guid newSessionId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _bySession.Remove(actor.SessionId);
        actor.SessionId = newSessionId;
        _bySession[newSessionId] = actor;
    }

    /// <summary>
    /// Puts a character in another room, and returns anyone whose autofollow this broke.
    /// </summary>
    /// <param name="walked">
    /// True only when this is the character stepping through an exit under their own power. False
    /// — the default — means a relocation nobody could have walked behind: a recall, a portal, a
    /// <c>goto</c>, a respawn, a room deleted underneath them. Those break every autofollow pointed
    /// at this character.
    /// </param>
    /// <remarks>
    /// <b>The default is the breaking one, deliberately.</b> A relocation site added later gets the
    /// safe behaviour without knowing this feature exists — the same argument an absent room flag
    /// resolving to the confining value already makes. Getting it wrong the other way leaves a
    /// follower standing three realms from the person they are following, still following them.
    ///
    /// Returns the dropped followers rather than telling them, because <see cref="WorldState"/> is
    /// state and does not narrate. A caller that ignores the list still gets the link broken, which
    /// is the half that must not be forgettable; saying so is the half that can be.
    /// </remarks>
    public IReadOnlyList<PlayerActor> Move(PlayerActor actor, RoomKey destination, bool walked = false)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var dropped = walked ? [] : BreakFollowersOf(actor.CharacterId);

        OccupantList(actor.RoomKey).Remove(actor);
        actor.Character.RoomKey = destination;
        OccupantList(destination).Add(actor);

        return dropped;
    }

    // -----------------------------------------------------------------------
    // Autofollow (PLAN.md §4.17)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Follower character id -> the leader they are following. Runtime only: an intent formed for
    /// one trip across a zone has no business surviving a relog.
    /// </summary>
    private readonly Dictionary<Guid, Guid> _following = [];

    /// <summary>Who this character is following, or null.</summary>
    public Guid? LeaderOf(Guid followerId) =>
        _following.TryGetValue(followerId, out var leader) ? leader : null;

    public void SetFollowing(Guid followerId, Guid leaderId) => _following[followerId] = leaderId;

    public bool StopFollowing(Guid followerId) => _following.Remove(followerId);

    /// <summary>Everyone currently following this character.</summary>
    public IReadOnlyList<PlayerActor> FollowersOf(Guid leaderId)
    {
        List<PlayerActor>? followers = null;

        foreach (var (followerId, id) in _following)
        {
            if (id == leaderId && FindByCharacter(followerId) is { } actor)
            {
                (followers ??= []).Add(actor);
            }
        }

        return followers ?? (IReadOnlyList<PlayerActor>)[];
    }

    /// <summary>Drops every autofollow pointed at this character, and returns who was dropped.</summary>
    public IReadOnlyList<PlayerActor> BreakFollowersOf(Guid leaderId)
    {
        var dropped = FollowersOf(leaderId);

        foreach (var follower in dropped)
        {
            _following.Remove(follower.CharacterId);
        }

        return dropped;
    }

    private List<PlayerActor> OccupantList(RoomKey key)
    {
        if (!_occupants.TryGetValue(key, out var list))
        {
            list = [];
            _occupants[key] = list;
        }

        return list;
    }

    /// <summary>Adds a mob to a room.</summary>
    public void AddMob(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var roomKey = RoomKey.Parse(mob.RoomKey);
        _mobs[mob.Id] = mob;
        MobListFor(roomKey).Add(mob);

        if (mob.SpawnerId is { } spawnerId)
        {
            SpawnedListFor(spawnerId).Add(mob.Id);
        }
    }

    /// <summary>Removes a mob from the world.</summary>
    public void RemoveMob(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var roomKey = RoomKey.Parse(mob.RoomKey);
        _mobs.Remove(mob.Id);
        MobListFor(roomKey).Remove(mob);

        // Whatever was on it dies with it. Left behind, a corpse's wounds and debuffs sat in the
        // table until the next expiry sweep noticed them - state belonging to something that no
        // longer exists, and one more thing for a sweep to walk.
        _activeEffects.Remove(mob.Id);

        if (mob.SpawnerId is { } spawnerId && _mobsBySpawner.TryGetValue(spawnerId, out var spawned))
        {
            spawned.Remove(mob.Id);

            if (spawned.Count == 0)
            {
                _mobsBySpawner.Remove(spawnerId);
            }
        }
    }

    /// <summary>
    /// How many living mobs this spawner is responsible for, anywhere in the world.
    /// </summary>
    /// <remarks>
    /// <b>Anywhere</b>, not in the spawner's own rooms. The sweep used to count what was standing
    /// in the rooms it fills, so a rat that wandered one room east stopped counting and the
    /// spawner replaced it - and the replacement wandered off too. A spawner set to three could
    /// populate a whole zone with rats given long enough, which is the flooding this fixes.
    ///
    /// Indexed rather than scanned, because the alternative is every spawner walking every mob in
    /// the world on every sweep - quadratic in exactly the situation the bug produced.
    /// </remarks>
    public int MobsFromSpawner(Guid spawnerId) =>
        _mobsBySpawner.TryGetValue(spawnerId, out var spawned) ? spawned.Count : 0;

    private HashSet<Guid> SpawnedListFor(Guid spawnerId)
    {
        if (!_mobsBySpawner.TryGetValue(spawnerId, out var spawned))
        {
            spawned = [];
            _mobsBySpawner[spawnerId] = spawned;
        }

        return spawned;
    }

    /// <summary>Moves a mob to a new room.</summary>
    public void MoveMob(Mob mob, RoomKey destination)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var current = RoomKey.Parse(mob.RoomKey);
        MobListFor(current).Remove(mob);
        mob.RoomKey = destination.ToString();
        MobListFor(destination).Add(mob);
    }

    /// <summary>All mobs in a specific room.</summary>
    public IReadOnlyList<Mob> MobsIn(RoomKey key) =>
        _mobsByRoom.TryGetValue(key, out var list) ? list : [];

    /// <summary>All mobs currently in the world.</summary>
    public IEnumerable<Mob> AllMobs => _mobs.Values;

    public Mob? FindMob(Guid mobId) => _mobs.GetValueOrDefault(mobId);

    private List<Mob> MobListFor(RoomKey key)
    {
        if (!_mobsByRoom.TryGetValue(key, out var list))
        {
            list = [];
            _mobsByRoom[key] = list;
        }

        return list;
    }

    /// <summary>Adds an item instance to a room.</summary>
    public void AddItem(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.RoomKey is not null)
        {
            var roomKey = RoomKey.Parse(item.RoomKey);
            _items[item.Id] = item;
            ItemListFor(roomKey).Add(item);
        }
    }

    /// <summary>Removes an item from the world.</summary>
    public void RemoveItem(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Remove(item.Id);
        if (item.RoomKey is not null)
        {
            var roomKey = RoomKey.Parse(item.RoomKey);
            ItemListFor(roomKey).Remove(item);
        }
    }

    /// <summary>All items in a specific room.</summary>
    public IReadOnlyList<ItemInstance> ItemsIn(RoomKey key) =>
        _itemsByRoom.TryGetValue(key, out var list) ? list : [];

    /// <summary>All items currently in the world.</summary>
    public IEnumerable<ItemInstance> AllItems => _items.Values;

    public ItemInstance? FindItem(Guid itemId) => _items.GetValueOrDefault(itemId);

    /// <summary>All items in a character's inventory.</summary>
    public IReadOnlyList<ItemInstance> InventoryOf(Guid characterId) =>
        _items.Values.Where(i => i.OwnerCharacterId == characterId).ToList();

    /// <summary>All items equipped on a character.</summary>
    public IReadOnlyList<ItemInstance> EquipmentOf(Guid characterId) =>
        _items.Values.Where(i => i.OwnerCharacterId == characterId && i.EquippedSlot != null).ToList();

    /// <summary>Moves an item to a character's inventory.</summary>
    public void PickUpItem(ItemInstance item, Guid characterId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.RoomKey is not null)
        {
            var roomKey = RoomKey.Parse(item.RoomKey);
            ItemListFor(roomKey).Remove(item);
        }

        item.RoomKey = null;
        item.OwnerCharacterId = characterId;

        // Whatever it was worn in, it is not worn now. See DropItem for why this belongs here.
        item.EquippedSlot = null;
    }

    /// <summary>Moves an item to the ground in a room.</summary>
    public void DropItem(ItemInstance item, RoomKey room)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.OwnerCharacterId = null;
        item.RoomKey = room.ToString();

        // Leaving a character's possession takes it off them, and the invariant is enforced here
        // rather than only in the commands because `EquippedSlot` is the *only* record of what
        // somebody is wearing and EquipmentResolver sums across every item carrying one, with no
        // per-slot dedup. The occupied-slot check in `wear` scans by ownership, so an item that
        // left a pack still holding a slot is invisible to the check and counts toward armour the
        // moment it comes back — which is exactly the stacking exploit this closes (BUGS.md #8).
        //
        // The commands refuse outright as well, so a player is told rather than quietly undressed.
        // Both halves are needed: this one covers instances that arrive from a builder spawn or an
        // import carrying a stale slot, which no command guard can reach.
        item.EquippedSlot = null;

        ItemListFor(room).Add(item);
    }

    /// <summary>Equips an item on a character.</summary>
    public void EquipItem(ItemInstance item, ItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.EquippedSlot = slot;
    }

    /// <summary>Unequips an item from a character.</summary>
    public void UnequipItem(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.EquippedSlot = null;
    }

    private List<ItemInstance> ItemListFor(RoomKey key)
    {
        if (!_itemsByRoom.TryGetValue(key, out var list))
        {
            list = [];
            _itemsByRoom[key] = list;
        }

        return list;
    }

    /// <summary>Get or create a combat instance for a room.</summary>
    public Combat GetOrCreateCombat(RoomKey roomKey)
    {
        if (!_combatsByRoom.TryGetValue(roomKey, out var combat))
        {
            combat = new Combat { RoomKey = roomKey };
            _combatsByRoom[roomKey] = combat;
        }
        return combat;
    }

    /// <summary>Find an existing combat in a room, or null if none.</summary>
    public Combat? FindCombat(RoomKey roomKey) =>
        _combatsByRoom.TryGetValue(roomKey, out var combat) ? combat : null;

    /// <summary>All active combats in the world.</summary>
    public IEnumerable<Combat> AllCombats => _combatsByRoom.Values;

    /// <summary>Remove a combat (when it ends).</summary>
    public void EndCombat(RoomKey roomKey) => _combatsByRoom.Remove(roomKey);

    /// <summary>Get all active effects on an entity.</summary>
    public IReadOnlyList<ActiveEffect> GetActiveEffects(Guid entityId) =>
        _activeEffects.TryGetValue(entityId, out var list) ? list : [];

    /// <summary>
    /// Whether this entity is stunned and so takes no turn at all.
    /// </summary>
    /// <remarks>
    /// Asked in three places - the combat loop, the cast command, and the mob AI - and each one
    /// answering for itself is how a stun ends up stopping swings but not spells. The expiry is
    /// checked here rather than trusted to the sweep, which runs on the 60s tick: a stun measured
    /// in a couple of swings would otherwise outlive itself by most of a minute.
    /// </remarks>
    public bool IsStunned(Guid entityId, long currentPulse) =>
        _activeEffects.TryGetValue(entityId, out var list) &&
        list.Any(e => e.PreventsActing && e.ExpiresAtPulse > currentPulse);

    /// <summary>
    /// Whether this entity is snared and so cannot leave the room or flee a fight.
    /// </summary>
    /// <remarks>
    /// Expiry is checked here for the same reason as <see cref="IsStunned"/>: the sweep that
    /// removes effects runs on the 60s tick, and a snare measured in seconds would otherwise
    /// hold long after it was supposed to let go.
    /// </remarks>
    public bool IsRooted(Guid entityId, long currentPulse) =>
        _activeEffects.TryGetValue(entityId, out var list) &&
        list.Any(e => e.PreventsEscape && e.ExpiresAtPulse > currentPulse);

    /// <summary>The name of whatever is holding this entity, for narration.</summary>
    public string? RootName(Guid entityId, long currentPulse) =>
        _activeEffects.TryGetValue(entityId, out var list)
            ? list.FirstOrDefault(e => e.PreventsEscape && e.ExpiresAtPulse > currentPulse)?.Name
            : null;

    /// <summary>Apply a new effect to an entity, handling stacking rules.</summary>
    public void ApplyEffect(Guid entityId, ActiveEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!_activeEffects.TryGetValue(entityId, out var effects))
        {
            effects = [];
            _activeEffects[entityId] = effects;
        }

        // Find existing effect by key and source
        var existing = effects.FirstOrDefault(e => e.EffectKey == effect.EffectKey && e.SourceEntityId == effect.SourceEntityId);

        if (existing is null)
        {
            // No existing effect, add new one
            effects.Add(effect);

            // The one place a maximum-health buff grants the health that comes with it: on first
            // application, never on a refresh. Raising the ceiling alone would be a buff that does
            // nothing at the moment you need it - 40/100 becoming 40/150 is further from safety -
            // and granting it again on every recast would make the ability a heal on a short
            // cooldown, which is the shape it was written as before it could be two effects.
            //
            // Here rather than in the executor because only this method can tell the two apart.
            GrantMaxHealth(entityId, effect);
        }
        else if (effect.SourceUnlockLevel > existing.SourceUnlockLevel)
        {
            // A stronger application of the same effect from the same caster replaces the weaker
            // one outright - its magnitude and its expiry, as though the weaker had never run.
            //
            // Refreshing was the old answer and it kept the *first* effect's numbers, so Sanctuary
            // cast over Fortitude was worth +150 rather than +220: the weaker buff won and then
            // outlived itself. Every one of a Path's maximum-health buffs collides here, because
            // this method dedupes on (EffectKey, SourceEntityId) and they share both.
            //
            // Remove-then-add rather than mutation, because ActiveEffect is init-only for
            // everything that matters - and because it is also the correct arithmetic: revoking
            // takes the old ceiling back down and clamps health to it, and granting puts the new
            // one up as a first application. A player who was at 140/200 on a 120 base ends at
            // 320/320, exactly where the stronger buff alone would have left them.
            effects.Remove(existing);
            RevokeMaxHealth(entityId, existing);

            effects.Add(effect);
            GrantMaxHealth(entityId, effect);
        }
        else if (effect.SourceUnlockLevel < existing.SourceUnlockLevel)
        {
            // A weaker application does nothing at all - it does not stack, and it does not extend
            // what is already running. Extending was the real cost of the old behaviour: a Temper
            // could hold Hemorrhage's 16-damage bleed open indefinitely by re-applying a cheap
            // Ambush, which refreshed the clock and left the bigger tick in place.
        }
        else
        {
            // Same strength - which is a recast of the same ability, or two effects with no ability
            // behind them at all. Mob attack riders are the second case: they leave the level at
            // zero, compare equal to each other, and so keep exactly the behaviour they always had.
            switch (existing.StackingRule)
            {
                case EffectStackingRule.Refresh:
                    // Just reset expiry, keep everything else
                    existing.ExpiresAtPulse = effect.ExpiresAtPulse;
                    break;

                case EffectStackingRule.Stack:
                    // Add stack if below max, update expiry
                    if (existing.Stacks < existing.MaxStacks)
                    {
                        existing.Stacks++;
                    }
                    existing.ExpiresAtPulse = effect.ExpiresAtPulse;
                    break;

                case EffectStackingRule.Ignore:
                    // Do nothing, existing effect continues unchanged
                    break;
            }
        }
    }

    /// <summary>Remove and return all expired effects across all entities.</summary>
    /// <summary>Raises the bearer's ceiling and hands them the health to go under it.</summary>
    private void GrantMaxHealth(Guid entityId, ActiveEffect effect)
    {
        if (effect.MaxHealthDelta <= 0 || VitalsOf(entityId) is not { } vitals)
        {
            return;
        }

        vitals.HealthMax += effect.MaxHealthDelta;
        vitals.Health += effect.MaxHealthDelta;
    }

    /// <summary>Lowers the ceiling again, and never leaves health above it.</summary>
    /// <remarks>
    /// The floor of 1 is deliberate: an authored delta larger than the bearer's whole maximum
    /// would otherwise expire them to zero and kill somebody with a buff running out, which is not
    /// a death anything in §4.12 describes.
    /// </remarks>
    private void RevokeMaxHealth(Guid entityId, ActiveEffect effect)
    {
        if (effect.MaxHealthDelta <= 0 || VitalsOf(entityId) is not { } vitals)
        {
            return;
        }

        vitals.HealthMax = Math.Max(1, vitals.HealthMax - effect.MaxHealthDelta);
        vitals.Health = Math.Min(vitals.Health, vitals.HealthMax);
    }

    /// <summary>The vitals behind an effect's entity id, whichever kind of thing it is.</summary>
    private Domain.Characters.Vitals? VitalsOf(Guid entityId) =>
        GetCharacter(entityId)?.Vitals ?? FindMob(entityId)?.Vitals;

    /// <summary>
    /// Removes every effect whose time is up, and returns each one <b>with whoever was carrying
    /// it</b>.
    /// </summary>
    /// <remarks>
    /// The bearer is the whole point of the pairing. This used to return bare effects, and the
    /// only entity id left on one is <see cref="ActiveEffect.SourceEntityId"/> — the caster. So
    /// the expiry system narrated to the caster, and every debuff a player landed on a mob ended
    /// with the player being told <em>their</em> effect had faded. A Warden's Sunder expiring on a
    /// corpse announced itself to the Warden.
    /// </remarks>
    public IReadOnlyList<(Guid EntityId, ActiveEffect Effect)> ExpireEffects(long currentPulse)
    {
        var expired = new List<(Guid, ActiveEffect)>();

        foreach (var entityId in _activeEffects.Keys.ToList())
        {
            var effects = _activeEffects[entityId];
            var toRemove = effects.Where(e => e.ExpiresAtPulse <= currentPulse).ToList();

            foreach (var effect in toRemove)
            {
                effects.Remove(effect);
                expired.Add((entityId, effect));

                // The ceiling comes down with the effect, and health clamps under it: 150/150
                // becomes 100/100, not 150/100. Done here rather than by the expiry system so a
                // future path that removes an effect some other way cannot forget it.
                RevokeMaxHealth(entityId, effect);
            }

            // Clean up empty lists
            if (effects.Count == 0)
            {
                _activeEffects.Remove(entityId);
            }
        }

        return expired;
    }

    /// <summary>Get a character by ID (format: c_<guid>).</summary>
    public Character? GetCharacter(Guid characterId) => FindByCharacter(characterId)?.Character;

    /// <summary>Get a mob by ID.</summary>
    public Mob? GetMob(Guid mobId) => FindMob(mobId);

    /// <summary>Load quests for a character from the repository result (called on EnterWorld).</summary>
    public void LoadCharacterQuests(Guid characterId, IEnumerable<CharacterQuest> quests)
    {
        if (!_questsByCharacter.TryGetValue(characterId, out var questDict))
        {
            questDict = [];
            _questsByCharacter[characterId] = questDict;
        }

        foreach (var quest in quests)
        {
            questDict[quest.QuestKey] = quest;
        }
    }

    /// <summary>Load a character's inventory and equipped items from the database.</summary>
    public void LoadCharacterItems(Guid characterId, IEnumerable<ItemInstance> items)
    {
        foreach (var item in items)
        {
            _items[item.Id] = item;
        }
    }

    /// <summary>Get all quests for a character, grouped by status.</summary>
    public IReadOnlyList<CharacterQuest> QuestsFor(Guid characterId) =>
        _questsByCharacter.TryGetValue(characterId, out var quests) ? quests.Values.ToList() : [];

    /// <summary>Get the state of a specific quest for a character, or null if not started.</summary>
    public CharacterQuest? GetQuestState(Guid characterId, string questKey)
    {
        if (_questsByCharacter.TryGetValue(characterId, out var quests))
        {
            quests.TryGetValue(questKey, out var quest);
            return quest;
        }
        return null;
    }

    /// <summary>Set or update the state of a quest for a character.</summary>
    public void SetQuestState(Guid characterId, string questKey, CharacterQuest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        if (!_questsByCharacter.TryGetValue(characterId, out var quests))
        {
            quests = [];
            _questsByCharacter[characterId] = quests;
        }

        quests[questKey] = quest;
    }

    /// <summary>
    /// Forgets a character's state for one quest, returning it to "never started".
    /// </summary>
    /// <remarks>
    /// §6: no row means not started, and Status is only Active or Completed. So abandoning is a
    /// removal rather than a third status - which is what makes the quest offerable again with no
    /// migration, no new enum member, and nothing else in the code needing to learn a new state.
    /// </remarks>
    public bool RemoveQuestState(Guid characterId, string questKey) =>
        _questsByCharacter.TryGetValue(characterId, out var quests)
        && quests.Remove(questKey);
}

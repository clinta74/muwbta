using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;

namespace Muwbta.Engine.Mutations;

/// <summary>
/// A world-content edit (PLAN.md §7.3). Builder endpoints turn a request into one of these and
/// hand it to the game loop, which is the only thing allowed to mutate world state (§2.1).
/// </summary>
/// <remarks>
/// Split into <em>requests</em>, which are what a builder asks for, and <em>primitives</em>,
/// which are what actually happened. The loop normalises the former into an ordered list of the
/// latter, applies them to memory, and hands the same list to persistence - so the database
/// replays exactly the operations memory performed, in the same order, rather than the two
/// sides independently interpreting the request and drifting apart.
///
/// The clearest case is <see cref="DigRoom"/>: the loop picks the new room's key, so only a
/// normalised list can tell persistence which key to write.
/// </remarks>
public abstract record WorldChange
{
    /// <summary>For the audit row: "world", "zone", "room", or "exit".</summary>
    public abstract string EntityKind { get; }

    public abstract string EntityKey { get; }
}

// ---------------------------------------------------------------------------
// Primitives - applied to memory and replayed into the database verbatim
// ---------------------------------------------------------------------------

public sealed record UpsertWorld(
    string Key,
    string Name,
    string Description,
    int SortOrder,
    FlagSet Flags,
    Multipliers Multipliers) : WorldChange
{
    public override string EntityKind => "world";

    public override string EntityKey => Key;
}

public sealed record DeleteWorld(string Key) : WorldChange
{
    public override string EntityKind => "world";

    public override string EntityKey => Key;
}

public sealed record UpsertZone(
    string Key,
    string WorldKey,
    string Name,
    string Description,
    int MinLevel,
    int MaxLevel,
    FlagSet Flags,
    Multipliers Multipliers) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

public sealed record DeleteZone(string Key) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

/// <summary>
/// Creates or replaces a room's own fields. Deliberately does not carry exits: those are
/// separate primitives, so editing a description can never silently rewrite the exit graph.
/// </summary>
public sealed record UpsertRoom(
    RoomKey Key,
    string ZoneKey,
    string Title,
    string Description,
    FlagSet Flags,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int? EditorX,
    int? EditorY) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => Key.ToString();
}

/// <summary>
/// Removes a room and the exits leading out of it. Exits pointing <em>at</em> it are left
/// dangling on purpose - that is the state §7.4 makes the world tolerate, and rewriting other
/// rooms' exits as a side effect of a delete would be a surprise.
/// </summary>
public sealed record DeleteRoom(RoomKey Key) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => Key.ToString();
}

/// <summary>Creates or repoints a single exit. One edge, one direction.</summary>
/// <param name="RequiredFlagKey">A character flag needed to pass, or null (PLAN.md §4.15).</param>
/// <param name="RequiredItemKey">An item the character must carry, or null.</param>
/// <param name="RefusalMessage">What someone turned away is told, or null for the generic line.</param>
/// <remarks>
/// The three conditions ride on <c>SetExit</c> rather than on a separate <c>SetExitCondition</c>
/// change, because an exit and its conditions are one row and one audit entry. Two primitives
/// would mean a gate could be half-saved — an exit that exists with the requirement still to
/// land — which is the only window in which it is open to everyone.
/// </remarks>
public sealed record SetExit(
    RoomKey From,
    Direction Direction,
    RoomKey To,
    string? RequiredFlagKey = null,
    string? RequiredItemKey = null,
    string? RefusalMessage = null) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

public sealed record RemoveExit(RoomKey From, Direction Direction) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

// ---------------------------------------------------------------------------
// Requests - normalised by the loop into the primitives above
// ---------------------------------------------------------------------------

/// <summary>
/// Walk-and-build (PLAN.md §7.6). One request covering two situations, because the builder
/// should not have to know which one they are in:
/// <list type="bullet">
/// <item><description><b>Materialize</b> - an exit already points this way at a room that does
/// not exist, so the new room takes the key the exit already names and the link resolves.</description></item>
/// <item><description><b>Dig</b> - there is no exit that way, so both the room and the exit to
/// reach it are created.</description></item>
/// </list>
/// </summary>
public sealed record DigRoom(
    RoomKey From,
    Direction Direction,
    bool Reciprocal = true,
    string? ZoneKey = null,
    RoomKey? NewRoomKey = null) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => From.ToString();
}

/// <summary>Links an exit, optionally with its reciprocal - the default (PLAN.md §7.6).</summary>
/// <param name="ApplyConditions">
/// Whether the three condition fields mean anything (PLAN.md §4.15). <b>False is "leave whatever is
/// there"</b>, which is what the in-game <c>link</c> verb and walk-and-build want: those say where a
/// door goes and nothing about who may use it, so repointing a locked exit must not unlock it.
/// True is "these are the conditions now", including null meaning none - what a builder's PUT of a
/// whole exit means, and the only way a lock can ever be removed.
/// </param>
/// <param name="ReciprocalConditions">
/// Whether <paramref name="ApplyConditions"/> also applies to the reciprocal edge. Defaults false
/// even though <paramref name="Reciprocal"/> defaults true: a corridor you cannot walk back down is
/// almost never meant, but you can always leave a vault.
/// </param>
public sealed record LinkExit(
    RoomKey From,
    Direction Direction,
    RoomKey To,
    bool Reciprocal = true,
    bool ApplyConditions = false,
    string? RequiredFlagKey = null,
    string? RequiredItemKey = null,
    string? RefusalMessage = null,
    bool ReciprocalConditions = false) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

public sealed record UnlinkExit(
    RoomKey From,
    Direction Direction,
    bool Reciprocal = true) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

/// <summary>
/// Changes a room's key, rewriting every exit that pointed at the old one in the same mutation
/// (PLAN.md §7.6) - otherwise renaming a dug room would silently orphan its neighbours.
/// </summary>
public sealed record RenameRoom(RoomKey From, RoomKey To) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => From.ToString();
}

/// <summary>Sets or clears one flag on one room. A null value clears it (PLAN.md §4.10).</summary>
public sealed record SetRoomFlag(RoomKey Key, string Flag, bool? Value) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => Key.ToString();
}

/// <summary>
/// Sets or clears one flag on one zone, leaving its siblings alone. A null value clears it, which
/// is not the same as setting it false - the key is removed, so the world above decides (§4.10).
/// </summary>
/// <remarks>
/// The alternative is what the builder did before this existed: read the whole flag map, edit one
/// key, and PATCH it back. Two builders working in one zone then erase each other, and the loss is
/// silent - the second write simply carries an older map. Rooms already had this primitive; the
/// scopes above them are where the blast radius is largest, so they needed it more.
/// </remarks>
public sealed record SetZoneFlag(string Key, string Flag, bool? Value) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

/// <summary>
/// Sets or clears one flag on one world. See <see cref="SetZoneFlag"/> - same shape, one level up.
/// </summary>
public sealed record SetWorldFlag(string Key, string Flag, bool? Value) : WorldChange
{
    public override string EntityKind => "world";

    public override string EntityKey => Key;
}

/// <summary>
/// Sets or clears the rendered sheet for one world (<see cref="Domain.Worlds.WorldMap"/>).
/// </summary>
/// <remarks>
/// The one change here the live world does not act on. A map is served to a player who asks for
/// it and is never read by the loop, so the applier passes this straight through to persistence
/// rather than touching any state - the same shape a configuration edit takes when it is not the
/// live one.
/// </remarks>
public sealed record SetWorldMap(string Key, string? Svg) : WorldChange
{
    public override string EntityKind => "map";

    public override string EntityKey => Key;
}

/// <summary>
/// Takes down everything this zone's spawners put in the world and fills them again at once
/// (PLAN.md §7.5).
/// </summary>
/// <remarks>
/// <para>
/// The one change here that edits nothing. Multipliers resolve once, at spawn time (§4.4), so a
/// difficulty edit reaches the next spawn and never the mob already standing in the room — and
/// without this the only ways to see the numbers just typed are to clear the zone by hand or to
/// restart the server.
/// </para>
/// <para>
/// <b>Nothing is persisted</b>, because nothing authored has changed: mobs live in memory alone,
/// so the applied list comes back empty and the writer is never called. It travels as a
/// <see cref="WorldChange"/> anyway because it mutates the world, and the loop is the only thread
/// allowed to do that (§2.1).
/// </para>
/// <para>
/// <b>Hand-placed mobs are left standing.</b> A mob with no spawner behind it has nothing to put
/// it back, so despawning one would be a delete wearing a refresh's name.
/// </para>
/// </remarks>
public sealed record RespawnZone(string Key) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

// ---------------------------------------------------------------------------
// Templates and Spawners
// ---------------------------------------------------------------------------

public sealed record UpsertMobTemplate(
    string Key,
    string Name,
    string Description,
    string Icon,
    int Level,
    int WanderIntervalPulses,
    Dictionary<string, object> BaseStats,
    int BaseXp,
    int BaseGold,
    Dictionary<string, object> Behavior,
    List<Dictionary<string, object>> Loot,
    List<MobAttack> Attacks) : WorldChange
{
    public override string EntityKind => "mob-template";

    public override string EntityKey => Key;
}

public sealed record DeleteMobTemplate(string Key) : WorldChange
{
    public override string EntityKind => "mob-template";

    public override string EntityKey => Key;
}

/// <summary>
/// Creates or retunes one ability (PLAN.md §4.5).
/// </summary>
/// <remarks>
/// Carries the whole row rather than a patch, like every other upsert here: the loop replaces what
/// it holds, so a partial change would have to be merged in two places and the two would disagree.
/// The builder API is what fills in the fields a request left out, from what is stored.
/// </remarks>
public sealed record UpsertAbility(
    string Key,
    CharacterPath Path,
    int UnlockLevel,
    string Name,
    string Description,
    CostType CostType,
    int CostValue,
    long CooldownPulses,
    int? CooldownGroup,
    long? CastTimePulses,
    TargetingType TargetingType,
    List<AbilityEffectSpec> Effects) : WorldChange
{
    public override string EntityKind => "ability";

    public override string EntityKey => Key;
}

public sealed record DeleteAbility(string Key) : WorldChange
{
    public override string EntityKind => "ability";

    public override string EntityKey => Key;
}

public sealed record UpsertItemTemplate(
    string Key,
    string Name,
    string Description,
    string Icon,
    List<ItemSlot> Slots,
    bool IsTwoHanded,
    int Weight,
    int BaseValue,
    Dictionary<string, object> BaseStats,
    int? AttackDelayPulses,
    string? AttackVerb,
    bool IsQuestItem,
    bool IsLore,
    bool IsNoDrop,
    bool IsLightSource,
    int? FoodValue,
    int? DrinkValue,
    List<CharacterPath> Paths) : WorldChange
{
    public override string EntityKind => "item-template";

    public override string EntityKey => Key;
}

public sealed record DeleteItemTemplate(string Key) : WorldChange
{
    public override string EntityKind => "item-template";

    public override string EntityKey => Key;
}

/// <summary>Creates or replaces a spawner (PLAN.md §4.8).</summary>
/// <remarks>
/// <see cref="NameModifier"/> has no default, on purpose: every site that builds one of these
/// states every field, so a field added here and forgotten at a call site is a compile error
/// rather than a quiet reset on the next room rename.
/// </remarks>
public sealed record UpsertSpawner(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    bool? Wanders,
    int? FightsAtLevel,
    string? NameModifier) : WorldChange
{
    public override string EntityKind => "spawner";

    public override string EntityKey => Id.ToString();
}

public sealed record DeleteSpawner(Guid Id) : WorldChange
{
    public override string EntityKind => "spawner";

    public override string EntityKey => Id.ToString();
}

public sealed record UpsertQuest(
    string Key,
    string? ZoneKey,
    string Name,
    string Summary,
    string Description,
    string GiverMobKey,
    string TurninMobKey,
    string? RequiredItemKey,
    int RequiredCount,
    int RewardXp,
    int RewardGold,
    string? RewardItemKey,
    int RewardItemCount,
    string? RewardFlagKey,
    List<string> PrerequisiteQuestKeys,
    bool IsRepeatable,
    bool AutoStart,
    List<CharacterPath> Paths,
    Dictionary<string, string> Dialogue,
    int SortOrder) : WorldChange
{
    public override string EntityKind => "quest";

    public override string EntityKey => Key;
}

public sealed record DeleteQuest(string Key) : WorldChange
{
    public override string EntityKind => "quest";

    public override string EntityKey => Key;
}

/// <summary>
/// A named starter configuration: where a new character wakes up, and what they are told
/// (PLAN.md §4.16).
/// </summary>
/// <remarks>
/// A mutation rather than a plain database write, so a builder changing the active configuration
/// takes effect for the next person to log in without a restart. The loop owns
/// <see cref="EngineOptions"/> the same way it owns the world, and going around it would leave a
/// running server disagreeing with its own database until somebody bounced it - precisely the
/// failure this setting exists to end.
///
/// Carries no active flag. Which configuration is live is environment state rather than content,
/// so it moves only through <see cref="ActivateGameConfiguration"/> - see the entity for why an
/// import must never repoint a live server as a side effect.
/// </remarks>
/// <param name="Live">
/// True when this is the configuration the server is currently obeying, in which case the edit
/// applies to the running loop as well as to the row. The caller decides, because the applier has
/// no database and asking it to know which row is active would mean giving it one.
/// </param>
/// <summary>Creates or replaces a starter configuration (PLAN.md §4.16).</summary>
/// <remarks>
/// <c>Canon</c> is the assist's canon, or null to leave the stored one as it is. Nullable because a
/// scoped bundle deliberately does not carry it, and an import that merged a realm must not wipe
/// the canon a builder wrote in the panel. The API always sends a string; empty means "use the
/// built-in one". <c>WorldKeys</c> follows the same rule: null leaves the stored list alone, and
/// an empty list clears it. <c>BlockedWords</c> too, since it stopped travelling in bundles: an
/// import must not touch what a server refuses to hear, so only the panel ever sends a value.
/// </remarks>
public sealed record UpsertGameConfiguration(
    string Key,
    string Name,
    string Description,
    string StartingRoomKey,
    string WelcomeMessage,
    string? BlockedWords,
    string? Canon,
    List<string>? WorldKeys,
    bool Live) : WorldChange
{
    public override string EntityKind => "configuration";

    public override string EntityKey => Key;
}

public sealed record DeleteGameConfiguration(string Key) : WorldChange
{
    public override string EntityKind => "configuration";

    public override string EntityKey => Key;
}

/// <summary>
/// Makes one configuration the live one, and every other one not.
/// </summary>
/// <remarks>
/// Separate from the upsert on purpose. Editing what a configuration says and choosing which one
/// the server obeys are different decisions with different blast radii - the first is a typo away
/// from a bad greeting, the second is a typo away from every new character waking up in the wrong
/// world - and they audit better as two entries than as one.
/// </remarks>
/// <remarks>
/// Carries the values as well as the key, rather than naming a row for the applier to go and read.
/// The loop has no database by design (§2.1), and the alternative — a configuration cache beside
/// the spawner and template caches — is a lot of machinery for two strings that only ever change
/// when a person clicks a button.
/// </remarks>
public sealed record ActivateGameConfiguration(
    string Key,
    string StartingRoomKey,
    string WelcomeMessage,
    string BlockedWords,
    string Canon) : WorldChange
{
    public override string EntityKind => "configuration";

    public override string EntityKey => Key;
}

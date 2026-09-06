using System.Globalization;
using System.Text.Json.Serialization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Server.Infrastructure;

namespace Muwbta.Server.Building;

// ---------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------

public sealed record WorldResponse(
    string Key,
    string Name,
    string Description,
    int SortOrder,
    IReadOnlyDictionary<string, bool> Flags,
    Multipliers Multipliers,
    int ZoneCount)
{
    public static WorldResponse From(World world, int zoneCount) =>
        new(world.Key, world.Name, world.Description, world.SortOrder, Flat(world.Flags),
            world.Multipliers.Clone(), zoneCount);

    internal static IReadOnlyDictionary<string, bool> Flat(FlagSet flags) =>
        RoomFlags.All
            .Where(f => flags.BooleanOrNull(f.Key) is not null)
            .ToDictionary(f => f.Key, f => flags.BooleanOrNull(f.Key)!.Value, StringComparer.Ordinal);
}

public sealed record ZoneResponse(
    string Key,
    string WorldKey,
    string Name,
    string Description,
    int MinLevel,
    int MaxLevel,
    IReadOnlyDictionary<string, bool> Flags,
    Multipliers Multipliers,
    int RoomCount)
{
    public static ZoneResponse From(Zone zone, int roomCount) =>
        new(zone.Key, zone.WorldKey, zone.Name, zone.Description, zone.MinLevel, zone.MaxLevel,
            WorldResponse.Flat(zone.Flags), zone.Multipliers.Clone(), roomCount);
}

public sealed record ExitResponse(
    string Direction,
    string To,
    bool TargetExists,
    string? RequiredFlagKey = null,
    string? RequiredItemKey = null,
    string? RefusalMessage = null);

/// <summary>
/// A room as the builder sees it. Carries <em>resolved</em> flags alongside the room's own, so
/// the editor can grey out an inherited value and name where it came from (PLAN.md §4.10) -
/// otherwise a room that is PvP because of its zone looks identical to one that is not.
/// </summary>
public sealed record RoomResponse(
    string Key,
    string ZoneKey,
    string Title,
    string Description,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyList<ResolvedFlag> Resolved,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int? EditorX,
    int? EditorY,
    IReadOnlyList<ExitResponse> Exits);

public sealed record ResolvedFlag(string Key, bool Value, string Source, string Summary);

public sealed record RoomFlagResponse(string Key, bool Default, string Summary, string Phase);

/// <summary>
/// The bundle format this build reads, so a client can compare before uploading.
/// </summary>
/// <remarks>
/// One field, and deliberately not a build number or a commit. The question a builder holding a file
/// actually has is "will this server take it", and the format version answers exactly that — where a
/// build string would need the reader to know which builds changed the format. If more of the
/// server's identity is ever wanted here, it belongs beside this rather than instead of it.
/// </remarks>
public sealed record BundleFormatResponse(int FormatVersion);

/// <summary>
/// What a zone respawn moved (PLAN.md §7.5). Two counts rather than one: a zone that was below
/// its population target comes back above where it was, and saying so is the difference between
/// a button that reports what it did and one that reports what it was asked to do.
/// </summary>
public sealed record RespawnZoneResponse(string ZoneKey, int Despawned, int Spawned);

/// <summary>An advisory warning. Never blocks a save (PLAN.md §7.4).</summary>
public sealed record ValidationWarning(string Kind, string EntityKey, string Message);

public sealed record ZoneValidation(
    string ZoneKey,
    IReadOnlyList<ValidationWarning> Warnings);

public sealed record UnfinishedRoom(string Key, string Title, int? EditorX, int? EditorY);

public sealed record AuditEntry(
    Guid Id,
    Guid? AccountId,
    string? Username,
    string EntityKind,
    string EntityKey,
    string Action,
    DateTimeOffset At);

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

/// <remarks>
/// <paramref name="Multipliers"/> is all-or-nothing: null leaves the stored set alone, and a
/// value replaces the whole set. It is not merged field by field, because
/// <see cref="Domain.Worlds.Multipliers"/> defaults every unspecified factor to 1.0 — a partial
/// object would silently reset the factors it omitted. The editor sends all eight.
/// </remarks>
public sealed record SaveWorldRequest(
    string? Name,
    string? Description,
    int? SortOrder,
    IReadOnlyDictionary<string, bool>? Flags,
    Multipliers? Multipliers);

/// <inheritdoc cref="SaveWorldRequest"/>
public sealed record SaveZoneRequest(
    string? WorldKey,
    string? Name,
    string? Description,
    int? MinLevel,
    int? MaxLevel,
    IReadOnlyDictionary<string, bool>? Flags,
    Multipliers? Multipliers);

public sealed record SaveRoomRequest(
    string? ZoneKey,
    string? Title,
    string? Description,
    IReadOnlyDictionary<string, bool>? Flags,
    IReadOnlyList<string>? Grid,
    IReadOnlyDictionary<string, string>? Legend,
    int? EditorX,
    int? EditorY);

/// <summary>
/// One exit, whole (PLAN.md §4.15). The three conditions are absolute rather than patch-style:
/// sending null clears them, which is what makes the editor able to remove a lock.
/// </summary>
/// <remarks>
/// <b><see cref="ReciprocalConditions"/> defaults to false while <see cref="Reciprocal"/> defaults
/// to true</b>, and the asymmetry is the point. Digging and linking are two-way by default because
/// a corridor you cannot walk back down is almost never what was meant; a lock is one-way by
/// default because you can always leave a vault. A builder who wants both sides gated says so.
/// </remarks>
public sealed record SaveExitRequest(
    string? To,
    bool Reciprocal = true,
    string? RequiredFlagKey = null,
    string? RequiredItemKey = null,
    string? RefusalMessage = null,
    bool ReciprocalConditions = false);

/// <summary>
/// One flag, three states, at whichever of the three scopes the route names. <c>true</c> and
/// <c>false</c> are decisions made here; <c>null</c> removes the key so the level above decides.
/// </summary>
/// <remarks>
/// This exists so the editor never has to send a whole flag map to change one flag.
/// <see cref="SaveRoomRequest.Flags"/> and its world and zone equivalents replace the entire set,
/// which quietly discards whatever another builder set in the meantime.
/// </remarks>
public sealed record SetFlagRequest(bool? Value);

/// <summary>PLAN.md §7.6. Every field optional: <c>{ "direction": "north" }</c> is the common case.</summary>
public sealed record DigRequest(
    string? Direction,
    bool Reciprocal = true,
    string? ZoneKey = null,
    string? NewRoomKey = null);

public sealed record RenameRoomRequest(string? NewKey);

// ---------------------------------------------------------------------------
// Templates and Spawners
// ---------------------------------------------------------------------------

public sealed record MobTemplateResponse(
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
    List<MobAttack> Attacks);

public sealed record SaveMobTemplateRequest(
    string? Name,
    string? Description,
    string? Icon,
    int? Level,
    int? WanderIntervalPulses,
    Dictionary<string, object>? BaseStats,
    int? BaseXp,
    int? BaseGold,
    Dictionary<string, object>? Behavior,
    List<Dictionary<string, object>>? Loot,
    List<MobAttack>? Attacks);

/// <param name="Problems">
/// What the validator says about this ability as stored. Carried on the response rather than only
/// returned from a save, so the editor can show a warning about a row somebody else authored, or
/// one that arrived by import — the two cases where nobody saw a save-time refusal.
/// </param>
public sealed record AbilityResponse(
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
    IReadOnlyList<AbilityEffectSpec> Effects,
    IReadOnlyList<AbilityProblemResponse> Problems);

public sealed record AbilityProblemResponse(string Severity, string Message);

/// <remarks>
/// The three enums carry <see cref="NullableEnumConverter{T}"/> for the same reason
/// <see cref="SaveItemTemplateRequest"/>'s slot does: a nullable enum sent as a string does not
/// bind without it, so <c>"path": "Warden"</c> would arrive as null and the save would be refused
/// for having no Path — a 400 that blames the caller for the server's deserialiser.
/// </remarks>
public sealed record SaveAbilityRequest(
    [property: JsonConverter(typeof(NullableEnumConverter<CharacterPath>))]
    CharacterPath? Path,
    int? UnlockLevel,
    string? Name,
    string? Description,
    [property: JsonConverter(typeof(NullableEnumConverter<CostType>))]
    CostType? CostType,
    int? CostValue,
    long? CooldownPulses,

    /// <remarks>
    /// Deliberately not coalesced against what is stored, for the same reason
    /// <see cref="CastTimePulses"/> is not: null <em>means</em> "shares no timer", so
    /// <c>?? existing</c> would make a timer impossible to clear from the editor.
    /// </remarks>
    int? CooldownGroup,

    long? CastTimePulses,
    [property: JsonConverter(typeof(NullableEnumConverter<TargetingType>))]
    TargetingType? TargetingType,
    List<AbilityEffectSpec>? Effects);

public sealed record ItemTemplateResponse(
    string Key,
    string Name,
    string Description,
    string Icon,
    // Names, not numbers. The single-slot field carried a converter for exactly this reason:
    // without it the enum serialised as an integer while the client read a string, and Head -
    // being 0 - was falsy the whole way through. A list has the same problem per element, and
    // JsonStringEnumConverter is the list-shaped answer to it.
    [property: JsonConverter(typeof(JsonStringEnumListConverter<ItemSlot>))]
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
    IReadOnlyList<CharacterPath> Paths);

public sealed record SaveItemTemplateRequest(
    string? Name,
    string? Description,
    string? Icon,
    [property: JsonConverter(typeof(JsonStringEnumListConverter<ItemSlot>))]
    List<ItemSlot>? Slots,
    bool? IsTwoHanded,
    int? Weight,
    int? BaseValue,
    Dictionary<string, object>? BaseStats,
    int? AttackDelayPulses,
    string? AttackVerb,
    bool? IsQuestItem,
    bool? IsLore,
    bool? IsNoDrop,
    bool? IsLightSource,
    int? FoodValue,
    int? DrinkValue,
    List<CharacterPath>? Paths);

/// <param name="Wander">One of <see cref="WanderMode"/>. Never null on the way out.</param>
public sealed record SpawnerResponse(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    string Wander,
    /// <summary>
    /// The level mobs from this spawner will fight at (PLAN.md §4.7). Zero for an item spawner, or
    /// for a mob spawner whose template or zone has since been deleted.
    /// </summary>
    /// <remarks>
    /// Read-only: the server computes it, and a client sending one back is ignored. It is here so
    /// the room's spawner list can say what a placement will actually produce, which a template's
    /// authored level does not answer once a zone has scaled it. It reports the outcome whether it
    /// was pinned or derived — <see cref="Level"/> is the one that says which.
    /// </remarks>
    int FightsAtLevel,
    /// <summary>
    /// Where <see cref="FightsAtLevel"/> came from: <see cref="SpawnLevel.Zone"/>, or the pinned
    /// number as text. Never null.
    /// </summary>
    string Level,
    /// <summary>
    /// The word put after the article of the template's name for these mobs, or null for the
    /// template's name as it is (PLAN.md §4.8).
    /// </summary>
    string? NameModifier,
    /// <summary>
    /// What a mob from this spawner is called, modifier applied. Null for an item spawner, or for
    /// a mob spawner whose template has since been deleted.
    /// </summary>
    /// <remarks>
    /// Read-only and server-composed, like <see cref="FightsAtLevel"/>: the rule that puts the
    /// word after the article lives in one place, and the browser showing "a marsh brigand" is
    /// the whole reason to have the field.
    /// </remarks>
    string? SpawnsAs);

/// <param name="Wander">One of <see cref="WanderMode"/>, or null to leave it as it is.</param>
/// <param name="Level">
/// <see cref="SpawnLevel.Zone"/> to let the zone decide, a positive integer as text to pin the
/// level these mobs fight at, or null to leave it as it is.
/// </param>
public sealed record SaveSpawnerRequest(
    string? ZoneKey,
    string? TemplateKey,
    TemplateKind? TemplateKind,
    List<string>? RoomKeys,
    int? TargetCount,
    int? RespawnSeconds,
    string? Wander,
    string? Level,
    /// <summary>
    /// The word to put after the article of the template's name. Null leaves the stored answer
    /// alone; an empty string clears it; anything else is validated
    /// (<see cref="Muwbta.Domain.Inhabitants.MobNaming.Problem"/>) and stored trimmed.
    /// </summary>
    /// <remarks>
    /// Empty-means-clear rather than a word, because unlike <see cref="Level"/> the stored value
    /// is text, and an empty text field is what a builder produces when they clear one.
    /// </remarks>
    string? NameModifier);

/// <summary>
/// How a spawner answers "what level do these mobs fight at": let the zone decide, or pin it
/// (PLAN.md §4.7).
/// </summary>
/// <remarks>
/// <b>A word for the same reason <see cref="WanderMode"/> is one.</b> Null already spells "leave
/// this alone" on every field of <see cref="SaveSpawnerRequest"/>, and the stored value is
/// genuinely optional — so a nullable int on the wire could not tell "do not touch it" from "clear
/// the pin and go back to the zone". Two meanings on one wire value is how a builder clears a
/// setting by not touching it.
///
/// Two states today. A third — <em>"always match the template, whatever it is retuned to"</em> — is
/// one branch here and one option in the dialog, precisely because the wire carries a word rather
/// than a number. It is deferred rather than dismissed: pinning 25 to match a level-25 template
/// works until someone retunes that template to 30, and nothing says so. Until a builder has felt
/// that drift, the preview showing <c>level 25 → fights at 25</c> side by side is the cheaper
/// answer.
/// </remarks>
public static class SpawnLevel
{
    /// <summary>Whatever the zone's dials work out to. The default, and the usual answer.</summary>
    public const string Zone = "zone";

    /// <summary>The stored value as the word or number that describes it.</summary>
    public static string From(int? level) =>
        level is { } pinned ? pinned.ToString(CultureInfo.InvariantCulture) : Zone;

    /// <summary>
    /// The word or number as a stored value. False when it is neither.
    /// </summary>
    /// <remarks>
    /// <see cref="NumberStyles.None"/> and the invariant culture, so <c>"+27"</c>, <c>" 27"</c>,
    /// <c>"27.0"</c> and <c>"1e2"</c> are all refused rather than quietly coerced into something
    /// nearby. A level is typed by a person; a typo should bounce rather than be interpreted.
    ///
    /// Zero and negatives are refused here rather than floored. <c>MobLevel</c> floors its own
    /// arithmetic because that value is derived, and there is nobody to tell; this one was typed,
    /// and silently correcting it would hide the mistake behind a plausible mob.
    /// </remarks>
    public static bool TryParse(string? level, out int? pinned)
    {
        pinned = null;

        if (level == Zone)
        {
            return true;
        }

        if (!int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < 1)
        {
            return false;
        }

        pinned = parsed;
        return true;
    }
}

/// <summary>
/// How a spawner answers "do these mobs wander": defer to the template, or override it.
/// </summary>
/// <remarks>
/// <b>A word rather than a nullable bool, because null already means something on this API.</b>
/// Every field of <see cref="SaveSpawnerRequest"/> is optional and a PATCH coalesces it against
/// what is stored - <c>request.X ?? existing.X</c> - so null spells *"leave this alone"* on every
/// other field. The stored value is genuinely three-valued
/// (<see cref="Domain.Spawning.Spawner.Wanders"/>), and a nullable bool would have made
/// "leave it alone" and "follow the template" the same request. Two meanings on one wire value is
/// how a builder clears a setting by not touching it.
/// </remarks>
public static class WanderMode
{
    /// <summary>Whatever the mob template says. The default, and the usual answer.</summary>
    public const string Template = "template";

    /// <summary>These mobs wander, whatever the template says.</summary>
    public const string Always = "always";

    /// <summary>These mobs stay put, whatever the template says.</summary>
    public const string Never = "never";

    /// <summary>The stored tri-state as the word that describes it.</summary>
    public static string From(bool? wanders) => wanders switch
    {
        true => Always,
        false => Never,
        null => Template,
    };

    /// <summary>
    /// The word as a stored tri-state. False when it is not one of the three.
    /// </summary>
    /// <remarks>
    /// Try-parse rather than a function returning the value, because what it parses to is itself
    /// nullable: "follow the template" *is* null. A method returning <c>bool?</c> would have to
    /// spell "that is not a mode" as null too, and a typo would silently mean the default.
    /// </remarks>
    public static bool TryParse(string? mode, out bool? wanders)
    {
        switch (mode)
        {
            case Template:
                wanders = null;
                return true;
            case Always:
                wanders = true;
                return true;
            case Never:
                wanders = false;
                return true;
            default:
                wanders = null;
                return false;
        }
    }
}

public sealed record QuestResponse(
    string Key,
    string ZoneKey,
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
    IReadOnlyList<CharacterPath> Paths,
    Dictionary<string, string> Dialogue,
    int SortOrder)
{
    /// <summary>
    /// One mapping, three callers. Written out at each of them until adding a field broke all
    /// three at once, which is the argument.
    /// </summary>
    public static QuestResponse From(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        return new QuestResponse(
            quest.Key, quest.ZoneKey, quest.Name, quest.Summary, quest.Description,
            quest.GiverMobKey, quest.TurninMobKey, quest.RequiredItemKey, quest.RequiredCount,
            quest.RewardXp, quest.RewardGold, quest.RewardItemKey, quest.RewardItemCount,
            quest.RewardFlagKey, quest.PrerequisiteQuestKeys, quest.IsRepeatable, quest.AutoStart,
            [.. quest.Paths], quest.Dialogue, quest.SortOrder);
    }
}

/// <summary>
/// One thing that would stop a quest being finishable. Advisory only, like every other builder
/// check - an unfinishable quest is still saveable, it just should not be a surprise.
/// </summary>
/// <param name="Kind">Machine-readable discriminator, e.g. "unreachable-required-item".</param>
/// <param name="Message">A sentence a builder can act on.</param>
/// <summary>
/// One room a spawner fills. <paramref name="Title"/> is null when the key names no room — a
/// spawner pointing at a deleted room is allowed (§7.4) and worth seeing, since it is the
/// difference between "placed here" and "placed nowhere".
/// </summary>
public sealed record PlacementRoom(string Key, string? Title);

/// <summary>One spawner that places a template, and the rooms it places it into.</summary>
/// <param name="ZoneName">The zone's name, since a key is not what a builder reads.</param>
/// <param name="FightsAtLevel">
/// What mobs from this spawner actually fight at (§4.7). Zero for an item spawner, or for a mob
/// spawner whose zone has since been deleted.
/// </param>
public sealed record PlacementSpawner(
    Guid Id,
    string ZoneKey,
    string ZoneName,
    int TargetCount,
    int RespawnSeconds,
    int FightsAtLevel,
    IReadOnlyList<PlacementRoom> Rooms,
    /// <summary>
    /// What this placement calls the template's mobs once its name modifier is applied. Null for
    /// an item, and null when there is no modifier — the panel already shows the template's name.
    /// </summary>
    string? SpawnsAs = null);

/// <summary>
/// A mob an item comes from: its loot table, or its shop stock.
/// </summary>
/// <param name="Chance">The loot roll, or null when this is a shop line rather than a drop.</param>
/// <param name="Placed">
/// Whether any spawner places this mob. Loot on a mob nobody places is loot nobody can reach, and
/// it is the half of the answer that is invisible from either template's own editor.
/// </param>
public sealed record PlacementMob(string Key, string Name, bool Placed, double? Chance = null);

/// <summary>A quest that hands out this item, or asks for it.</summary>
/// <param name="Role">"reward" or "required".</param>
public sealed record PlacementQuest(string Key, string Name, string ZoneKey, string Role);

/// <summary>
/// Everywhere one template shows up in the authored world (PLAN.md §7.9).
/// </summary>
/// <remarks>
/// The three item-only lists are empty for a mob. An item without them would be answered "nowhere"
/// nearly always, because most items have no ground spawner of their own — they drop, they are
/// sold, or they are handed over at a turn-in.
/// </remarks>
public sealed record TemplatePlacement(
    string TemplateKey,
    string Kind,
    IReadOnlyList<PlacementSpawner> Spawners,
    IReadOnlyList<PlacementMob> DroppedBy,
    IReadOnlyList<PlacementMob> SoldBy,
    IReadOnlyList<PlacementQuest> Quests);

public sealed record ReachabilityWarning(
    string Kind,
    string Message,
    string? ItemKey = null,
    string? MobKey = null);

public sealed record QuestReachability(
    string QuestKey,
    IReadOnlyList<ReachabilityWarning> Warnings);

public sealed record SaveQuestRequest(
    string? ZoneKey,
    string? Name,
    string? Summary,
    string? Description,
    string? GiverMobKey,
    string? TurninMobKey,
    string? RequiredItemKey,
    int? RequiredCount,
    int? RewardXp,
    int? RewardGold,
    string? RewardItemKey,
    int? RewardItemCount,
    string? RewardFlagKey,
    List<string>? PrerequisiteQuestKeys,
    bool? IsRepeatable,
    bool? AutoStart,
    List<CharacterPath>? Paths,
    Dictionary<string, string>? Dialogue,
    int? SortOrder);

/// <summary>
/// Multiplier preview for a zone: shows how templates resolve with current multipliers.
/// Used for difficulty tuning in the builder UI.
/// </summary>
/// <param name="TemplateLevel">The level as authored. Zero for an item, which has no level.</param>
/// <param name="FightsAtLevel">
/// The level a mob spawned here actually fights at (PLAN.md §4.7). The difference between this and
/// <paramref name="TemplateLevel"/> is the whole point of the panel — a rat lifted to 20 by a zone's
/// dials pays and reads as a level 20 fight, and there was previously nowhere to see that before a
/// player felt it.
/// </param>
/// <param name="BaseStats">The template's own jsonb bag, verbatim.</param>
/// <param name="BaseValues">
/// The same numbers <paramref name="ResolvedStats"/> reports, before scaling, keyed identically.
/// <para>
/// It exists because the panel cannot do the join itself. A template may write its dice as
/// <c>"damage": "4-7"</c>, so the resolved <c>damageMin</c> has no counterpart in
/// <paramref name="BaseStats"/> and the Base column would read "—" for the one dial this whole
/// panel is about. Synthesising the missing keys into the template's own bag was the alternative,
/// and a bag that claims to be the template while carrying keys the template never had is a lie
/// the next reader trips over.
/// </para>
/// </param>
public sealed record MultiplierPreviewRow(
    string TemplateKey,
    string TemplateName,
    TemplateKind Kind,
    Dictionary<string, object> BaseStats,
    Dictionary<string, int> ResolvedStats,
    int TemplateLevel,
    int FightsAtLevel,
    Dictionary<string, int> BaseValues);

public sealed record MultiplierPreview(
    string ZoneKey,
    Dictionary<string, decimal> WorldMultipliers,
    Dictionary<string, decimal> ZoneMultipliers,
    List<MultiplierPreviewRow> Templates);

// ---------------------------------------------------------------------------
// Named starter configurations (PLAN.md §4.16)
// ---------------------------------------------------------------------------

/// <param name="StartingRoomExists">
/// False when the starting room names a room this environment does not have. Advisory rather than
/// an error: writing a configuration *before* importing the world it points into is the ordinary
/// order of operations on a fresh server, so the panel warns and saves.
/// </param>
/// <param name="Canon">The assist's canon for this configuration, or empty for the built-in one.</param>
/// <param name="CanonTokens">
/// Roughly what <paramref name="Canon"/> costs the model (<c>Canon.EstimateTokens</c>); zero when
/// there is none. Against <see cref="GameConfigurationList.CanonTokenBudget"/>.
/// </param>
public sealed record GameConfigurationResponse(
    string Key,
    string Name,
    string Description,
    string StartingRoomKey,
    string WelcomeMessage,
    bool IsActive,
    bool StartingRoomExists,
    DateTimeOffset UpdatedAt,
    string Canon,
    int CanonTokens,
    /// <summary>The worlds this configuration is for; a world belongs to at most one.</summary>
    IReadOnlyList<string> WorldKeys);

/// <param name="ActiveStartingRoomKey">
/// What the running loop is obeying right now, which is not always what a row says. A database
/// with no active configuration leaves the engine on its configured fallback, and a panel showing
/// only an empty list would imply the server had no starting room at all.
/// </param>
/// <param name="CanonTokenBudget">What a canon may cost before the model stops reading all of it.</param>
/// <param name="CanonCharsPerToken">
/// The ratio the server estimates with, so the panel can show a live figure while somebody types
/// without keeping a second constant of its own.
/// </param>
public sealed record GameConfigurationList(
    IReadOnlyList<GameConfigurationResponse> Configurations,
    string ActiveStartingRoomKey,
    string ActiveWelcomeMessage,
    int CanonTokenBudget,
    double CanonCharsPerToken);


/// <param name="BlockedWords">
/// The word list, or null for none. Whole words, one per line or separated by commas; see
/// <c>WordFilter</c> for what is and is not matched.
/// </param>
/// <param name="Canon">
/// The assist's canon, or null to leave the stored one as it is. Empty means "use the built-in
/// one", which is how a builder goes back to it.
/// </param>
public sealed record GameConfigurationRequest(
    string Name,
    string? Description,
    string StartingRoomKey,
    string? WelcomeMessage,
    string? Canon = null,
    /// <summary>
    /// The worlds this configuration is for, or null to leave the stored list alone. A world
    /// another configuration already lists is refused, with its owner named.
    /// </summary>
    List<string>? WorldKeys = null);

/// <summary>What this server refuses to hear. One list, for the whole server.</summary>
/// <param name="BlockedWords">
/// One word per line, or separated by commas or spaces. Empty means no filter at all, which is a
/// real answer and not a missing one.
/// </param>
public sealed record ModerationResponse(string BlockedWords);

/// <summary>A replacement word list. Empty clears the filter.</summary>
public sealed record ModerationRequest(string? BlockedWords);

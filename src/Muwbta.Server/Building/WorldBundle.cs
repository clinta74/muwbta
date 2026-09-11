using System.Text.Json;
using System.Text.Json.Serialization;
using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Server.Infrastructure;

namespace Muwbta.Server.Building;

/// <summary>
/// The authored world as one JSON document (PLAN.md §6, Phase 6) - what moves content between
/// environments now that Postgres is the only source of truth and there are no world files.
/// </summary>
/// <remarks>
/// <para>
/// This carries <em>content</em> and nothing else: the same eight tables the retired SQL export
/// covered, for the same reasons. Accounts, characters, item
/// instances, character quests, and the two audit tables are player data and history, and an
/// import that resurrected deleted characters would be a bug rather than a feature.
/// </para>
/// <para>
/// <b>Abilities travel now, and used to be excluded.</b> The reason they were left out has
/// expired: <c>ReconcileAbilitiesAsync</c> rebuilt them from <c>AbilityCatalogue</c> on every
/// startup, so an imported row would have been corrected on the next boot. The reconcile now only
/// plants what is missing, which makes the table authoritative - and makes this bundle the way a
/// retune reaches another environment at all.
/// </para>
/// <para>
/// Entities are stored flat and keyed rather than nested under their parents. A room names its
/// zone rather than living inside it, which is what lets a bundle be scoped to a zone and still
/// carry the world above it, and what keeps the import order a property of the importer rather
/// than of the file. Exits are the one exception: they hang off their room, because an exit has
/// no identity of its own beyond the room and direction it leaves by.
/// </para>
/// </remarks>
public sealed record WorldBundle(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    BundleScope Scope,
    IReadOnlyList<BundleWorld> Worlds,
    IReadOnlyList<BundleZone> Zones,
    IReadOnlyList<BundleRoom> Rooms,
    IReadOnlyList<BundleItemTemplate> ItemTemplates,
    IReadOnlyList<BundleMobTemplate> MobTemplates,
    IReadOnlyList<BundleAbility> Abilities,
    IReadOnlyList<BundleSpawner> Spawners,
    IReadOnlyList<BundleQuest> Quests,
    IReadOnlyList<BundleGameConfiguration> Configurations,
    IReadOnlyList<BundleMap> Maps = null!)
{
    /// <summary>
    /// The rendered sheets, one per world at most, and the only optional collection here.
    /// </summary>
    /// <remarks>
    /// Optional because it is the one collection a hand-authored file is likely to leave out
    /// entirely - most content has no map and wants none - and a missing key deserialising to null
    /// would fail at the first thing that read it rather than at the file. Normalised on the way
    /// in so nothing downstream has to ask.
    /// </remarks>
    public IReadOnlyList<BundleMap> Maps { get; init; } = Maps ?? [];

    /// <summary>
    /// The only format this build writes, and the only one it reads.
    /// </summary>
    /// <remarks>
    /// Refusing an unknown version is the one hard refusal in the whole import path. Everything
    /// else is advisory (§7.4), but a bundle whose shape this build does not understand cannot
    /// be partially applied usefully - it would import the fields that happened to match and
    /// silently drop the rest, which is the failure mode a version number exists to prevent.
    ///
    /// <b>19 because a configuration hands out a starting kit.</b> <c>startingKit</c> is new on a
    /// configuration, and null means "leave the stored kit alone", so a v18 bundle read as v19 simply
    /// arrives with no opinion about the kit. The weak kind of bump: nothing is lost in that
    /// direction, but a v19 file read by a v18 server would drop the kit silently, which is the
    /// partial apply the number is here to refuse. The same bump carries <c>useEffects</c> and
    /// <c>useCooldownPulses</c> on an item - what a potion does when it is drunk - under the same
    /// rule: absent is none.
    ///
    /// <b>15 because an item names every slot it fits, and may claim two.</b> <c>slot</c> is gone
    /// and <c>slots</c> plus <c>twoHanded</c> stand in its place. <b>The strong kind of bump, in
    /// both directions.</b> A v14 bundle read as v15 has no <c>slots</c> key at all, so every item
    /// in it arrives equippable nowhere: a world of armour that cannot be worn and weapons that
    /// cannot be held, which is the loudest failure on this list and the only reason it is not the
    /// dangerous one. The other direction is the quiet one and it is why this is strong rather than
    /// weak - a v15 bundle read as v14 drops <c>twoHanded</c>, so every maul and staff in it
    /// becomes a one-handed weapon that leaves the off hand free, and a character wielding one with
    /// a shield is a rules change nothing announces.
    ///
    /// <b>14 because a quest can be for one Path.</b> A quest carries <c>paths</c>, and a giver
    /// offers only what the character in front of them can use. The weak kind of bump, like 13: a
    /// v13 bundle has no such key, so read as v14 every quest arrives unrestricted - which is
    /// exactly what a v13 quest meant. It is here because the exporter now writes a key a v13
    /// reader would drop. The other direction is the one worth refusing on: a v14 bundle read as
    /// v13 loses the restriction, and one smith starts handing every character all four epic
    /// chains again - four Path-locked rewards, three of them unusable and undroppable.
    ///
    /// <b>13 because abilities can share a timer.</b> An ability carries <c>cooldownGroup</c>, and
    /// using any ability on a timer puts the whole timer on cooldown. This is the *weak* kind of
    /// bump, like 5 and 7: a v12 bundle has no such key, so read as v13 every ability arrives
    /// ungrouped — which is exactly what a v12 ability meant, so nothing is silently lost. It is
    /// here because the exporter now writes a key a v12 reader would drop, and a file labelled 12
    /// that carries a v13 field is a lie about its own shape. The other direction is the one worth
    /// refusing on: a v13 bundle read as v12 loses the grouping, and a Warden whose four walls
    /// stopped sharing a timer is back to 470 seconds of continuous cover with nothing to say so.
    ///
    /// <b>12 because a weapon says what it hits for.</b> Weapons carry <c>damageMin</c> and
    /// <c>damageMax</c>, and the item-level <c>damageMultiplier</c> is gone. This is the strong kind
    /// of bump in both directions, which is unusual. A v11 bundle read as v12 loses every weapon's
    /// only damage stat and arrives as a field of fists — 1–2, whatever the author wrote. A v12
    /// bundle read as v11 is worse: the dice bind, but the *engine* on that build would still be
    /// looking for a multiplier and would find none, so it too swings 1–2. Neither direction throws,
    /// and both produce a world where nothing has a weapon. The number is what refuses them.
    ///
    /// <b>11 because a spawner says how rare its thing is.</b> <c>respawnSeconds</c> is back on
    /// every spawner, and this time something reads it. A v10 bundle has no such key, so read as
    /// v11 every spawner in it would arrive at the record's default — which is 60 and harmless, but
    /// the same is not true in the other direction: an authored hourly boss re-read as v10 would
    /// silently become a minute, and a version number is what stops that being invisible.
    ///
    /// <b>10 because three dials that never did anything are gone.</b> A v9 bundle carries
    /// <c>itemPower</c> and <c>spawnDensity</c> on its multipliers and <c>respawnSeconds</c> on
    /// every spawner. Read as v10 those keys are simply ignored, which is harmless — they were
    /// ignored by the engine too — but the bump is not about the reading. It is about the
    /// *writing*: an export at v10 no longer emits them, so a v9 file and a v10 file describe the
    /// same world with different fields, and a version number is what stops somebody diffing the
    /// two and concluding content was lost (BUGS.md #17).
    ///
    /// <b>18 because a server's blocked-words list stopped travelling.</b> A v17 bundle carries
    /// <c>blockedWords</c> on every configuration; a v18 one does not, and importing a v17 file
    /// here would silently apply somebody else's moderation policy to this server - or, exporting,
    /// hand yours to whoever you sent a realm to. What a server refuses to hear belongs to whoever
    /// runs it and to the people playing there, not to whoever authored the world, so it is
    /// shipped with the engine and seeded once (<see cref="Moderation.DefaultBlockedWords"/>) and
    /// the database is the authority after that. This is the strong kind of bump: both directions
    /// change a policy nobody would see change.
    ///
    /// <b>17 because a world carries the map of itself.</b> A v16 bundle has no <c>maps</c>, so
    /// read as v17 every world in it arrives with no sheet - which is the weak direction and
    /// harmless on its own. The bump is about the writing, and about what the sheets replaced: the
    /// maps used to be compiled into the server, which meant a build served whatever drawing it
    /// was built with while the rooms came from the database. The two could disagree and nothing
    /// could tell. Carried here they arrive together, and a file labelled 16 that carries a v17
    /// key would be a lie about the one property this change exists to establish.
    ///
    /// <b>16 because an item can be eaten.</b> A v15 bundle has no <c>foodValue</c> or
    /// <c>drinkValue</c>, so read as v16 every item in it arrives inedible. That is the weak
    /// direction and the right one - an item that silently stopped being food is a shop full of
    /// bread nobody can eat, which reads as a broken verb rather than as a dropped field.
    ///
    /// <b>9 because an item can be a lamp.</b> A v8 bundle has no <c>isLightSource</c>, so read
    /// as v9 every item in it arrives unlit. That is the weak direction and it is the one that
    /// matters: a dark room is unreadable without a light, so a bundle carrying the lantern that
    /// answers one of its own dark rooms would import a lantern that does nothing, and the room
    /// would look like a bug in the room rather than a field that was dropped.
    ///
    /// <b>8 because an item can refuse you.</b> A v7 bundle has no <c>isLore</c>, <c>isNoDrop</c>
    /// or <c>paths</c>, so read as v8 every item arrives unrestricted — which is the weak direction
    /// for two of the three and the wrong one for all of them: an epic reward that is neither lore
    /// nor bound is an epic reward one player farms and hands to five friends. The same shape as 6:
    /// the missing fields all deserialise to "no restriction", silently, and a bundle cannot say
    /// what it was not written to say.
    ///
    /// <b>7 because a bundle carries its own starter configurations.</b> A v6 bundle has no
    /// <c>configurations</c>, so read as v7 it would arrive with none — which is survivable, unlike
    /// the bumps below it, since a missing configuration is visible the moment somebody opens the
    /// panel. It is here because the exporter now *writes* a key a v6 reader would drop, and a file
    /// labelled 6 that carries a v7 field is a lie about its own shape. This is what lets a whole
    /// starter set move between servers — the Aldenmoor configuration and the Reaches one are two
    /// complete answers to "what does a new player meet", and swapping is the point (§4.16).
    ///
    /// <b>6 because an exit can refuse you and a quest can grant a capability.</b> A version 5
    /// bundle has no <c>requiredFlagKey</c>, <c>requiredItemKey</c> or <c>rewardFlagKey</c>, so
    /// read as v6 every one of them would deserialise to null - which is a gate that opens for
    /// anybody, and a chain that never attunes anyone to anything (§4.15). Both halves fail
    /// silently and in the dangerous direction, which is precisely what this number is here to
    /// refuse. The strong kind of bump, unlike the one below it.
    ///
    /// <b>5 because a spawner can pin the level its mobs fight at.</b> This is the weaker kind of
    /// bump, and worth saying so: a v4 bundle carries no pin, which reads correctly as "the zone
    /// decides" - exactly what a v4 spawner meant. It is here because the exporter now *writes* a
    /// key a v4 reader would drop, and a file labelled 4 that carries a v5 field is a lie about its
    /// own shape. Omitting the key when null was the alternative; it buys nothing and costs a
    /// serialisation special case.
    ///
    ///
    /// <b>4 because an ability carries a list of effects rather than one.</b> A version 3 bundle
    /// has <c>effectKey</c> and <c>effectParams</c> where this one has <c>effects</c>, so read as
    /// v4 every ability in it would arrive with an empty list - which is an ability that costs its
    /// resource and does nothing, the exact silent failure the version number is here to refuse.
    ///
    /// <b>3 because abilities travel now.</b> A version 2 bundle carries no abilities at all, so
    /// reading one as version 3 would import an empty ability list - and, if a "replace" mode ever
    /// existed, would read as "this environment should have none". Refusing is the honest answer:
    /// the older file genuinely cannot say what this one is being asked to.
    ///
    /// <b>2 because a spawner's wander setting changed shape and meaning.</b> Version 1 carried
    /// <c>sentinel: bool</c>, where false was the value every spawner had by default and meant
    /// *"these mobs wander"*. Version 2 carries <c>wanders: bool?</c>, where absent means *"follow
    /// the template"*. A v1 bundle read as v2 would deserialise the missing key to null and every
    /// spawner in it would quietly change behaviour - which is the silent partial apply this
    /// number exists to refuse, arriving through a rename rather than through a new field.
    /// </remarks>
    public const int CurrentFormatVersion = 19;
}

/// <summary>
/// A named starter configuration: where a new character wakes up, and what they are told
/// (PLAN.md §4.16).
/// </summary>
/// <remarks>
/// <b>There is deliberately no <c>isActive</c>.</b> The definitions are content and belong beside
/// the world they describe; which one a given server obeys is that environment's own state, like
/// which room a character is standing in. An import is a merge that runs against a server with
/// people on it, so a field arriving in a content file must never repoint where every new character
/// wakes up — activation stays an explicit call, audited as its own act.
///
/// Carried whole rather than scoped, like abilities: a configuration belongs to a server, not to a
/// zone, so a zone-scoped export carries all of them or none.
/// </remarks>
public sealed record BundleGameConfiguration(
    string Key,
    string Name,
    string Description,
    string StartingRoomKey,
    string WelcomeMessage,
    /// <summary>
    /// The assist's canon as the exporting server actually reads it - the row's own text, or the
    /// one compiled into that server when the row is empty. Carried by a full export and by a
    /// configuration's own; null in a realm's scoped bundle, and null on import means "leave the
    /// stored one alone", so a realm's file cannot blank what a builder wrote in the panel.
    /// Optional, and a missing key means "leave the stored one alone" rather than "blank it".
    /// </summary>
    string? Canon = null,
    /// <summary>
    /// The worlds this configuration is for, or null to leave the stored list alone. Carried by
    /// every export, since it is what a configuration <em>is</em> and costs a few bytes.
    /// </summary>
    List<string>? WorldKeys = null,
    /// <summary>
    /// What a new character is handed, or null to leave the stored kit alone. Carried by every
    /// export that carries the configuration.
    /// </summary>
    List<StartingKitItem>? StartingKit = null);

/// <summary>
/// What the export was asked for, recorded so a bundle can say what it is rather than leaving
/// the reader to infer it from what happens to be inside.
/// </summary>
/// <param name="Kind">"all", "world", or "zone".</param>
/// <param name="Key">The world or zone key, or null when the scope is everything.</param>
public sealed record BundleScope(string Kind, string? Key);

/// <summary>
/// One rendered sheet, drawn by <c>tools/render-map.cs</c> from the rooms it names.
/// </summary>
/// <param name="WorldKey">The world this draws. At most one sheet per world.</param>
/// <param name="Svg">
/// The SVG document itself. Title and intrinsic size are read back out of its own header rather
/// than authored beside it, so a sheet cannot claim a size it is not drawn at.
/// </param>
public sealed record BundleMap(string WorldKey, string Svg);

public sealed record BundleWorld(
    string Key,
    string Name,
    string Description,
    int SortOrder,
    // Raw JSON rather than a bool map, so a flag this build does not recognise survives the
    // round trip the same way it survives a database one (§4.10). An export that dropped a
    // newer binary's flag would quietly rewrite content on its way between environments.
    JsonElement Flags,
    IReadOnlyDictionary<string, decimal> Multipliers);

public sealed record BundleZone(
    string Key,
    string WorldKey,
    string Name,
    string Description,
    int MinLevel,
    int MaxLevel,
    JsonElement Flags,
    IReadOnlyDictionary<string, decimal> Multipliers);

public sealed record BundleRoom(
    string Key,
    string ZoneKey,
    string Title,
    string Description,
    JsonElement Flags,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int? EditorX,
    int? EditorY,
    IReadOnlyList<BundleExit> Exits);

public sealed record BundleExit(
    string Direction,
    string To,
    string? RequiredFlagKey = null,
    string? RequiredItemKey = null,
    string? RefusalMessage = null);

public sealed record BundleItemTemplate(
    string Key,
    string Name,
    string Description,
    string Icon,
    [property: JsonConverter(typeof(JsonStringEnumListConverter<ItemSlot>))]
    List<ItemSlot>? Slots,
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
    List<CharacterPath>? Paths,
    List<AbilityEffectSpec>? UseEffects = null,
    int? UseCooldownPulses = null);

/// <summary>
/// One ability, whole. Unlike a zone-scoped entity there is nothing to scope an ability *to* -
/// abilities belong to a Path, not to a zone - so a scoped export carries all of them or none.
/// </summary>
/// <remarks>
/// Carried in full rather than as a diff against the catalogue, for the reason the catalogue
/// stopped being authoritative: the target environment's catalogue may not be this build's, and a
/// bundle that only said "cooldown 48" would mean different things depending on what it landed
/// beside.
/// </remarks>
public sealed record BundleAbility(
    string Key,
    [property: JsonConverter(typeof(NullableEnumConverter<CharacterPath>))]
    CharacterPath? Path,
    int UnlockLevel,
    string Name,
    string Description,
    [property: JsonConverter(typeof(NullableEnumConverter<CostType>))]
    CostType? CostType,
    int CostValue,
    long CooldownPulses,
    int? CooldownGroup,
    long? CastTimePulses,
    [property: JsonConverter(typeof(NullableEnumConverter<TargetingType>))]
    TargetingType? TargetingType,
    List<AbilityEffectSpec>? Effects);

public sealed record BundleMobTemplate(
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

/// <remarks>
/// Carries its <see cref="Id"/>, which is what makes re-importing a bundle idempotent. A spawner
/// has no content key to collide on, so an import that minted a fresh id every time would double
/// the population of every zone it touched on the second run.
/// </remarks>
public sealed record BundleSpawner(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    bool? Wanders,
    /// <summary>
    /// The level these mobs fight at, or null to let the zone decide (PLAN.md §4.7).
    /// </summary>
    /// <remarks>
    /// A nullable int rather than the word <c>SpawnerResponse.Level</c> carries. A bundle is a copy
    /// of the database and has no PATCH semantics to disambiguate against, so null here means only
    /// one thing - the same reason <see cref="Wanders"/> is a <c>bool?</c> here and a word there.
    /// </remarks>
    int? FightsAtLevel,
    /// <summary>
    /// The word put after the article of the template's name for these mobs, or null for the
    /// template's name as it is (PLAN.md §4.8).
    /// </summary>
    /// <remarks>
    /// Optional, and the format version is deliberately not bumped for it: a bundle written before
    /// the field existed reads as "no modifier", which is exactly what those spawners did.
    /// </remarks>
    string? NameModifier = null);

public sealed record BundleQuest(
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
    List<CharacterPath>? Paths,
    Dictionary<string, string> Dialogue,
    int SortOrder);

// ---------------------------------------------------------------------------
// Import reporting
// ---------------------------------------------------------------------------

/// <summary>
/// What an import did, or - under <c>dryRun</c> - what it would have done.
/// </summary>
/// <param name="Counts">Per entity kind, split by whether the key already existed here.</param>
/// <param name="Warnings">
/// Advisory, exactly as <c>/validate</c> is (§7.4): a reference the bundle does not carry and
/// this database does not have either. Never blocks the import, because a zone legitimately
/// imported ahead of the zone it links to is a state the world already tolerates.
/// </param>
/// <param name="Failures">
/// Entities the loop refused or that could not be persisted. Non-empty means the import was
/// partial - see <see cref="WorldImporter"/> for why that is possible.
/// </param>
public sealed record ImportReport(
    int FormatVersion,
    bool DryRun,
    IReadOnlyList<ImportCount> Counts,
    IReadOnlyList<ValidationWarning> Warnings,
    IReadOnlyList<ImportFailure> Failures)
{
    public bool Ok => Failures.Count == 0;
}

/// <param name="Removed">
/// Exits the bundle's own rooms had and the bundle does not ask for, which the import deleted.
/// Zero for every other kind: exits are the one thing an import prunes, and
/// <see cref="WorldImporter"/> says why.
/// </param>
public sealed record ImportCount(string Kind, int Created, int Updated, int Removed = 0);

public sealed record ImportFailure(string Kind, string Key, string Message);

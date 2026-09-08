/**
 * The builder API (PLAN.md §7.3). Same-origin and cookie-authorised like everything else;
 * the server additionally requires the Builder role, so every call here can 403.
 */

import { request } from './api'

/**
 * One entry in the server's flag registry, which is what the flag panels render from.
 *
 * `kind` decides the control: a boolean gets the three-state on/off/inherit buttons, and a text
 * flag gets a list of `choices`. Adding a flag of either kind still reaches the UI with no client
 * change, which is the property §4.10 wanted from rendering off the registry in the first place.
 */
export interface RoomFlagDefinition {
  key: string
  kind: 'boolean' | 'text'
  default: FlagValue
  summary: string
  phase: string
  /** Every value a text flag may take, the default first. Empty for a boolean. */
  choices: string[]
}

/** What a flag can be set to. `null` removes the key so the level above decides. */
export type FlagValue = boolean | string

/** What bundle format the running server reads. See `builderApi.bundleFormat`. */
export interface BundleFormatInfo {
  formatVersion: number
}

/**
 * The §4.4 difficulty dial. Composition is `round(base × world × zone)`, so 1.0 everywhere is
 * "no change" and the two levels multiply rather than override.
 */
export interface Multipliers {
  /** Master dial: scales health and damage together. */
  strength: number
  health: number
  damage: number
  xp: number
  gold: number
  itemValue: number
  /** Spawner target counts — makes a zone crowded, not just tougher. */
}

export const MULTIPLIER_KEYS: Array<keyof Multipliers> = [
  'strength',
  'health',
  'damage',
  'xp',
  'gold',
  'itemValue',
]

export const NEUTRAL_MULTIPLIERS: Multipliers = {
  strength: 1,
  health: 1,
  damage: 1,
  xp: 1,
  gold: 1,
  itemValue: 1,
}

export interface WorldSummary {
  key: string
  name: string
  description: string
  sortOrder: number
  flags: Record<string, FlagValue>
  multipliers: Multipliers
  zoneCount: number
}

export interface ZoneSummary {
  key: string
  worldKey: string
  name: string
  description: string
  minLevel: number
  maxLevel: number
  flags: Record<string, FlagValue>
  multipliers: Multipliers
  roomCount: number
}

/** One template's stats before and after this zone's multipliers, for the §7.5 preview table. */
export interface MultiplierPreviewRow {
  templateKey: string
  templateName: string
  kind: string
  baseStats: Record<string, unknown>
  resolvedStats: Record<string, number>
  /** The same keys as `resolvedStats`, unscaled — `baseStats` cannot be joined against for a range. */
  baseValues: Record<string, number>
  /** Zero for an item, which has no level. */
  templateLevel: number
  /** What a mob spawned in this zone actually fights at (PLAN.md §4.7). */
  fightsAtLevel: number
}

export interface MultiplierPreview {
  zoneKey: string
  worldMultipliers: Record<string, number>
  zoneMultipliers: Record<string, number>
  templates: MultiplierPreviewRow[]
}

/**
 * What a zone respawn moved (PLAN.md §7.5).
 *
 * Two counts rather than one: a zone that was below its population target — a spawner still
 * waiting out its `respawnSeconds` — comes back above where it was, and saying so is the
 * difference between a button that reports what it did and one that reports what it was asked to
 * do.
 */
export interface ZoneRespawn {
  zoneKey: string
  despawned: number
  spawned: number
}

/**
 * One room a spawner fills. A null `title` means the key names no room — allowed (PLAN.md §7.4)
 * and worth seeing, since it is the difference between "placed here" and "placed nowhere".
 */
export interface PlacementRoom {
  key: string
  title: string | null
}

export interface PlacementSpawner {
  id: string
  zoneKey: string
  zoneName: string
  targetCount: number
  respawnSeconds: number
  /** What mobs from this spawner actually fight at (§4.7); 0 for an item spawner. */
  fightsAtLevel: number
  rooms: PlacementRoom[]
  /**
   * What this placement calls the template's mobs once its name modifier is applied (§4.8).
   * Null for an item, and null when there is no modifier — the panel is already headed by the
   * template's own name.
   */
  spawnsAs: string | null
}

/** A mob an item comes from: its loot table, or its shop stock. */
export interface PlacementMob {
  key: string
  name: string
  /** Whether any spawner places this mob. Loot on a mob nobody places is loot nobody can reach. */
  placed: boolean
  /** The loot roll, or null when this is a shop line rather than a drop. */
  chance: number | null
}

/** A quest that hands out this item, or asks for it. */
export interface PlacementQuest {
  key: string
  name: string
  zoneKey: string
  role: 'reward' | 'required'
}

/**
 * Everywhere one template shows up in the authored world (PLAN.md §7.9).
 *
 * The three item-only lists come back empty for a mob. They exist because most items have no
 * ground spawner of their own — they drop, they are sold, or they are handed over at a turn-in —
 * so an item answered by spawners alone would read "nowhere" nearly always.
 */
export interface TemplatePlacement {
  templateKey: string
  kind: 'mob' | 'item'
  spawners: PlacementSpawner[]
  droppedBy: PlacementMob[]
  soldBy: PlacementMob[]
  quests: PlacementQuest[]
}

export interface RoomExit {
  direction: string
  to: string
  /** False for a dangling link - allowed, and worth drawing differently (PLAN.md §7.4). */
  targetExists: boolean
  /** A character flag needed to pass, or null for a way anyone may take (PLAN.md §4.15). */
  requiredFlagKey: string | null
  /** An item template key the character must be carrying. Never consumed. */
  requiredItemKey: string | null
  /** What someone turned away is told. Null falls back to a generic line. */
  refusalMessage: string | null
}

/** What gates an exit. Every field null is an exit anyone may use (PLAN.md §4.15). */
export interface ExitConditions {
  requiredFlagKey: string | null
  requiredItemKey: string | null
  refusalMessage: string | null
  /**
   * Whether the conditions also apply to the reciprocal edge. Defaults false where `reciprocal`
   * defaults true, because you can always leave a vault.
   */
  reciprocalConditions?: boolean
}

/** Where a flag's effective value came from, so inherited values can be shown as inherited. */
export interface ResolvedFlag {
  key: string
  value: FlagValue
  source: 'room' | 'zone' | 'world' | 'default'
  summary: string
}

export interface RoomDetail {
  key: string
  zoneKey: string
  title: string
  description: string
  flags: Record<string, FlagValue>
  resolved: ResolvedFlag[]
  grid: string[]
  legend: Record<string, string>
  editorX: number | null
  editorY: number | null
  exits: RoomExit[]
}

export interface ValidationWarning {
  kind: string
  entityKey: string
  message: string
}

export interface ZoneValidation {
  zoneKey: string
  warnings: ValidationWarning[]
}

export interface UnfinishedRoom {
  key: string
  title: string
  editorX: number | null
  editorY: number | null
}

export interface AuditEntry {
  id: string
  accountId: string | null
  username: string | null
  entityKind: string
  entityKey: string
  action: string
  at: string
}

/** One of a mob's attacks. Each entry runs on its own timer. */
export interface MobAttack {
  /** Base-form verb: "bite" narrates "A wolf bites you". */
  verb: string
  /** Pulses between swings of this attack. Minimum 4 (1 second). */
  delayPulses: number
  /** Scales this attack against the mob's damage. Null means 1.0. */
  damageMultiplier: number | null
  /**
   * An effect applied on a landed hit, keyed as the engine's `EffectRegistry` knows it. Null for
   * a plain attack. This is how a mob stuns, snares, or bleeds — it has attacks rather than a
   * spellbook (PLAN.md §12).
   */
  effectKey: string | null
  /** Parameters for `effectKey`. Strings, because that is what the executors parse. */
  effectParams: Record<string, string> | null
}

export interface MobTemplate {
  key: string
  name: string
  description: string
  icon: string
  level: number
  wanderIntervalPulses: number
  baseStats: Record<string, unknown>
  baseXp: number
  baseGold: number
  loot: Array<Record<string, unknown>>
  behavior: Record<string, unknown>
  attacks: MobAttack[]
}

/**
 * Where an item can be equipped. Names rather than numbers on the wire: `Head` is 0 on the server,
 * so an integer slot was falsy the whole way through the editor.
 */
export type ItemSlot =
  | 'Head'
  | 'Chest'
  | 'Hands'
  | 'Legs'
  | 'Feet'
  | 'MainHand'
  | 'OffHand'
  | 'Trinket'

/** Every slot, in the server's enum order — which is also the order the hands are tried in. */
export const ITEM_SLOTS: ItemSlot[] = [
  'Head',
  'Chest',
  'Hands',
  'Legs',
  'Feet',
  'MainHand',
  'OffHand',
  'Trinket',
]

export interface ItemTemplate {
  key: string
  name: string
  description: string
  icon: string
  /**
   * Every slot it fits, in `ItemSlot` order — so the first hand in the list is the one the game
   * reaches for. **Empty means it cannot be equipped at all**, which is the opposite of `paths`
   * below: a slot list is a capability and starts at none, a Path list is a restriction and starts
   * at no restriction.
   */
  slots: ItemSlot[]
  /**
   * Wielding it claims the off hand too. Only ever set alongside a `slots` of exactly
   * `['MainHand']`; the server refuses any other combination rather than normalising it.
   */
  isTwoHanded: boolean
  weight: number
  baseValue: number
  baseStats: Record<string, unknown>
  /** Pulses between swings when wielded. Null means no declared speed. */
  attackDelayPulses: number | null
  /** Base-form verb describing how it strikes: "slash", "crush". */
  attackVerb: string | null
  /** Bound to a quest: cannot be sold or destroyed, but can still be dropped (PLAN.md §4.9). */
  isQuestItem: boolean
  /** One only, counting what is worn. Checked on pick-up, purchase, gift, and quest reward. */
  isLore: boolean
  /** Cannot be dropped or given away. Destroying it is still allowed, deliberately. */
  isNoDrop: boolean
  /** Lights the room while worn or wielded. Any slot; carrying it in the pack is not enough. */
  isLightSource: boolean
  /**
   * How much hunger `eat` answers, or null when it is not food. Null and 0 say different things:
   * null is inedible, 0 would be food worth nothing.
   */
  foodValue: number | null
  /** How much thirst `drink` answers, or null when it is not drink. */
  drinkValue: number | null
  /**
   * The Paths that may wear or wield this. **Empty means anyone**, which is why it is a list of
   * what is allowed rather than of what is forbidden — an item is unrestricted until a builder
   * opts in.
   *
   * Strings rather than numbers: the server registers a `JsonStringEnumConverter` globally, so
   * `CharacterPath` crosses the wire as "Warden" rather than 0.
   */
  paths: CharacterPath[]
}

/**
 * Whether mobs from a spawner wander. `template` defers to the mob template, which carries the
 * default (PLAN.md §4.8); the other two override it for this placement.
 *
 * A word rather than a boolean because the server's value is three-valued and every field of a
 * spawner PATCH is optional — a nullable bool could not tell "leave this alone" from "follow the
 * template".
 */
export type WanderMode = 'template' | 'always' | 'never'

/**
 * The four Paths, in the server enum's own order.
 *
 * A value rather than only a type, because a form that offers all four has to iterate them — and
 * a hand-written list beside the union is the pair that drifts. `CharacterPath` is derived from
 * this so the two cannot disagree.
 */
export const CHARACTER_PATHS = ['Warden', 'Adept', 'Temper', 'Hallow'] as const

export type CharacterPath = (typeof CHARACTER_PATHS)[number]
export type CostType = 'Focus' | 'Stamina' | 'Health'
export type TargetingType = 'SingleTarget' | 'Self' | 'Aoe'

/** What the validator says about a stored ability. Errors are refused on save. */
export interface AbilityProblem {
  severity: 'Error' | 'Warning'
  message: string
}

/** One effect an ability applies. PascalCase on the wire, matching the stored jsonb. */
export interface AbilityEffectSpec {
  key: string
  params: Record<string, string>
}

export interface Ability {
  key: string
  path: CharacterPath
  unlockLevel: number
  name: string
  description: string
  costType: CostType
  costValue: number
  cooldownPulses: number
  /**
   * A timer this ability shares with others on the same Path, or null when it shares none.
   *
   * Using any ability on the timer puts the whole timer on cooldown, for that ability's own
   * cooldown. Scoped to the Path, so Warden 1 and Temper 1 are different timers — a character only
   * ever knows one Path's abilities, so the two can never meet in play.
   */
  cooldownGroup: number | null
  castTimePulses: number | null
  targetingType: TargetingType
  /**
   * What the ability does, in order. One entry is the ordinary case; several let one ability do
   * several things — the reason Last Stand can raise maximum health *and* harden defence rather
   * than being written as a heal.
   */
  effects: AbilityEffectSpec[]
  /**
   * Carried on every read, not just returned from a save. A row can arrive by import or by hand,
   * and then nobody ever saw a refusal — so this list is the only place a builder finds out.
   */
  problems: AbilityProblem[]
}

export interface Spawner {
  id: string
  zoneKey: string
  templateKey: string
  templateKind: 'Mob' | 'Item'
  roomKeys: string[]
  targetCount: number
  /** Seconds before one lost instance is replaced. How rare the thing is. */
  respawnSeconds: number
  wander: WanderMode
  /**
   * What mobs from this spawner fight at (PLAN.md §4.7). Server-computed and read-only; zero for
   * an item spawner. Sending it back is ignored — see `SpawnerSave`.
   *
   * It reports the outcome whether the level was pinned or derived from the zone; `level` says
   * which.
   */
  fightsAtLevel: number
  /**
   * Where `fightsAtLevel` came from: `'zone'`, or the pinned level as a decimal string.
   *
   * A word rather than a nullable number for the reason `wander` is one — on a PATCH, null already
   * means "leave this alone", so a nullable number could not also spell "clear the pin".
   */
  level: string
  /**
   * One lower-case word put after the article of the template's name for mobs from this spawner
   * (PLAN.md §4.8): `marsh` makes "a brigand" spawn as "a marsh brigand". Null uses the template's
   * name as it is. On a PATCH, null leaves it alone and an empty string clears it.
   */
  nameModifier: string | null
  /**
   * What a mob from this spawner is called, modifier applied. Server-composed and read-only, like
   * `fightsAtLevel`; null for an item spawner or a deleted template.
   */
  spawnsAs: string | null
}

/**
 * The subset of a spawner a client may write.
 *
 * `createSpawner`/`updateSpawner` used to take `Partial<Spawner>`, which now advertises the
 * server-computed `fightsAtLevel` as though setting it did something. `spawnsAs` is the same
 * kind of field and is left out for the same reason.
 */
export type SpawnerSave = Partial<Omit<Spawner, 'id' | 'fightsAtLevel' | 'spawnsAs'>>

export interface Quest {
  key: string
  zoneKey: string
  name: string
  summary: string
  description: string
  giverMobKey: string
  turninMobKey: string
  requiredItemKey: string | null
  requiredCount: number
  rewardXp: number
  rewardGold: number
  rewardItemKey: string | null
  rewardItemCount: number
  /**
   * A character flag granted on completion, or null (PLAN.md §4.15). How attunement is earned,
   * and the only thing that opens a gate requiring that flag.
   */
  rewardFlagKey: string | null
  prerequisiteQuestKeys: string[]
  isRepeatable: boolean
  autoStart: boolean
  /**
   * The Paths this quest is for. **Empty means anyone**, the same way an item's does — a quest is
   * unrestricted until a builder opts in, so nothing authored before this field changed behaviour.
   *
   * It exists because the four epic chains have one giver and Path-locked rewards: every character
   * was handed all four, and finishing the wrong one produced a weapon they could not wield and,
   * being lore and no-drop, could not get rid of.
   */
  paths: CharacterPath[]
  dialogue: Record<string, string>
  sortOrder: number
}

/**
 * One reason a quest could not be finished. Advisory: an unfinishable quest still saves, it just
 * should not be a surprise (PLAN.md §7.4).
 */
export interface ReachabilityWarning {
  kind: string
  message: string
  itemKey: string | null
  mobKey: string | null
}

export interface QuestReachability {
  questKey: string
  warnings: ReachabilityWarning[]
}

/** A quest in the chain graph. `external` marks a prerequisite that lives in another zone. */
export interface StorylineNode {
  key: string
  name: string
  zoneKey: string
  external: boolean
}

/** `from` must be completed before `to` can be offered. */
export interface StorylineEdge {
  from: string
  to: string
}

export interface Storyline {
  zoneKey: string
  nodes: StorylineNode[]
  edges: StorylineEdge[]
  /** Quests sitting on a prerequisite cycle. Every one of them is unstartable. */
  cycles: string[]
  /** Quests whose prerequisites can never all be met. */
  unreachable: string[]
  /** Prerequisites naming a quest that does not exist. */
  missingPrerequisites: Array<{ quest: string; missing: string }>
}

/**
 * The four dialogue strings a quest can override (PLAN.md §4.9), keyed exactly as
 * `QuestCommands` reads them. Each falls back to generated prose when absent, so leaving one
 * blank is a real choice rather than a hole.
 */
export const DIALOGUE_KEYS = [
  'giverOffer',
  'giverInProgress',
  'giverComplete',
  'turninReady',
] as const

const base = '/api/builder'

/**
 * A named starter configuration (PLAN.md §4.16): where a new character wakes up and what the game
 * says to them. A server holds several and exactly one is live.
 */
export interface GameConfiguration {
  key: string
  name: string
  description: string
  startingRoomKey: string
  welcomeMessage: string
  isActive: boolean
  /**
   * False when the starting room names a room this server does not have. Advisory: writing a
   * configuration before importing the world it points into is the normal order of operations.
   */
  startingRoomExists: boolean
  updatedAt: string
  /**
   * What the builder assist is told about this world before every request, as markdown. Empty
   * means the assist is told there is no world description and to invent nothing. Activating the
   * configuration makes the assist read this one; editing it takes effect on the next request.
   */
  canon: string
  /** Roughly what `canon` costs the model; zero when there is none. */
  canonTokens: number
  /**
   * The worlds this configuration is for. A world belongs to at most one configuration, and
   * "Download bundle" on a configuration exports these worlds together with it and its canon.
   */
  worldKeys: string[]
}


/** The server's word list. Empty means no filter at all, which is an answer rather than a gap. */
export interface ModerationPolicy {
  blockedWords: string
}

export interface GameConfigurationList {
  configurations: GameConfiguration[]
  /** What the running loop is obeying, which is not always what a row says. */
  activeStartingRoomKey: string
  activeWelcomeMessage: string
  /** What a canon may cost before the model stops reading all of it. */
  canonTokenBudget: number
  /** The ratio the server estimates tokens with, so the panel can show a live figure while typing. */
  canonCharsPerToken: number
}

export interface ImportCount {
  kind: string
  created: number
  updated: number

  /**
   * Exits the bundle's own rooms had and the bundle did not ask for, which the import deleted.
   *
   * Always 0 for every other kind. Exits are keyed by room and direction, so re-authoring one as
   * a different direction writes the new key and leaves the old one behind - the server prunes
   * those and names each one as a `stale-exit` warning.
   */
  removed: number
}

export interface ImportFailure {
  kind: string
  key: string
  message: string
}

/** What an import did, or - under dryRun - what it would have done. */
export interface ImportReport {
  formatVersion: number
  dryRun: boolean
  counts: ImportCount[]
  warnings: ValidationWarning[]
  failures: ImportFailure[]
  ok: boolean
}

/**
 * A queued draft from the builder assist.
 *
 * A job rather than a response because generation is slow enough to matter: measured at 1.3-1.8
 * tokens a second, one room description is about three minutes. The server accepts the request,
 * returns an id, and the client asks again until it is done.
 */
export interface AssistJob {
  id: string
  state: 'Queued' | 'Warming' | 'Running' | 'Succeeded' | 'Failed'
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
  draft: RoomDraft | null
  /** Filled in instead of `draft` when the job was for a mob, item, or quest. */
  prose: ProseDraft | null
  error: string | null
  /** Things wrong with the draft that the output grammar could not prevent. Never fatal. */
  warnings: string[]
}

export interface RoomDraft {
  title: string
  description: string
  exits: Array<{ direction: string; to: string }>
}

export interface ProseDraft {
  name: string
  description: string
  /** Quests only. */
  summary: string | null
}

/** Which of the three kinds a prose draft is for. Matches the server's AssistSchema.ProseKind. */
export type ProseKind = 'Mob' | 'Item' | 'Quest'

/**
 * A prose draft request.
 *
 * The entity must already exist: its numbers are the context the description is written against,
 * and prose written without them contradicts the thing it describes.
 */
export interface ProseDraftRequest {
  kind: ProseKind
  key: string
  instruction?: string
  name?: string
  summary?: string
  description?: string
}

/** What the assist is told. The current editor buffer, not the saved row — see the server side. */
export interface RoomDraftRequest {
  zoneKey: string
  roomKey: string
  instruction?: string
  title?: string
  description?: string
}

/** A terrain kind the generator can draw. */
export interface TerrainKindInfo {
  key: string
  summary: string
}

/** A drawn map: the rows, and what each glyph in them means. */
export interface RoomTerrain {
  grid: string[]
  legend: Record<string, string>
}

export const builderApi = {
  roomFlags: () => request<RoomFlagDefinition[]>(`${base}/room-flags`),

  /**
   * The bundle format version this server accepts.
   *
   * Asked so the Transfer panel can compare a file against the server *before* uploading it. The
   * format version is the import path's only hard refusal, and without this the one way to find out
   * a server had not been updated yet was to send it a bundle and read the 400.
   */
  bundleFormat: () => request<BundleFormatInfo>(`${base}/bundle-format`),

  terrainKinds: () => request<TerrainKindInfo[]>(`${base}/terrain-kinds`),

  /**
   * Draws a room's terrain. Nothing is saved: the result comes back to be looked at and then
   * written through the same room PATCH a brush stroke uses.
   *
   * Seeded by the room key, so asking twice gives the same map.
   */
  roomTerrain: (key: string, kind: string) =>
    request<RoomTerrain>(`${base}/rooms/${key}/terrain/${kind}`),

  worlds: () => request<WorldSummary[]>(`${base}/worlds`),

  createWorld: (key: string, body: Partial<WorldSummary>) =>
    request<WorldSummary>(`${base}/worlds/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateWorld: (key: string, body: Partial<WorldSummary>) =>
    request<WorldSummary>(`${base}/worlds/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  /** @see setRoomFlag — same three states, one scope up. */
  setWorldFlag: (key: string, flag: string, value: FlagValue | null) =>
    request<WorldSummary>(`${base}/worlds/${key}/flags/${flag}`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),

  deleteWorld: (key: string) =>
    request<void>(`${base}/worlds/${key}`, { method: 'DELETE' }),

  zones: (worldKey?: string) =>
    request<ZoneSummary[]>(worldKey ? `${base}/zones?world=${worldKey}` : `${base}/zones`),

  createZone: (key: string, body: Partial<ZoneSummary>) =>
    request<ZoneSummary>(`${base}/zones/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateZone: (key: string, body: Partial<ZoneSummary>) =>
    request<ZoneSummary>(`${base}/zones/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  /** @see setRoomFlag — same three states, one scope up. */
  setZoneFlag: (key: string, flag: string, value: FlagValue | null) =>
    request<ZoneSummary>(`${base}/zones/${key}/flags/${flag}`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),

  deleteZone: (key: string) => request<void>(`${base}/zones/${key}`, { method: 'DELETE' }),

  /**
   * How every template in a zone resolves under the current multipliers (PLAN.md §7.5).
   * The endpoint has existed since Phase 3; nothing called it until the multipliers became
   * editable, so the panel it was written for never got built.
   */
  zonePreview: (zoneKey: string) =>
    request<MultiplierPreview>(`${base}/zones/${zoneKey}/preview`),

  /**
   * Clears out this zone's mob spawners and fills them again, so a multiplier edit is visible in
   * the world now rather than at the next respawn (PLAN.md §7.5).
   *
   * Multipliers resolve once, at spawn time (§4.4): a saved edit reaches the next spawn and never
   * the mob already standing in the room. This is the only thing that closes that gap short of a
   * restart.
   */
  respawnZone: (zoneKey: string) =>
    request<ZoneRespawn>(`${base}/zones/${zoneKey}/respawn`, { method: 'POST' }),

  rooms: (zoneKey: string) => request<RoomDetail[]>(`${base}/zones/${zoneKey}/rooms`),

  room: (key: string) => request<RoomDetail>(`${base}/rooms/${key}`),

  createRoom: (key: string, body: Record<string, unknown>) =>
    request<RoomDetail>(`${base}/rooms/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateRoom: (key: string, body: Record<string, unknown>) =>
    request<RoomDetail>(`${base}/rooms/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteRoom: (key: string) => request<void>(`${base}/rooms/${key}`, { method: 'DELETE' }),

  /**
   * Queues a room draft. 429 means the queue is full — one model, one worker, and minutes a job,
   * so "not now" is the honest answer rather than a promise nobody would wait for.
   *
   * 404 means the server was built without an assistant configured, which is a supported state:
   * the builder is expected to work unchanged without one.
   */
  draftRoom: (body: RoomDraftRequest) =>
    request<{ id: string }>(`${base}/assist/rooms`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  /** Mobs, items and quests share one endpoint, and one job reader with rooms. */
  draftProse: (body: ProseDraftRequest) =>
    request<{ id: string }>(`${base}/assist/prose`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  assistJob: (id: string) => request<AssistJob>(`${base}/assist/rooms/${id}`),

  /** 404 when this server has no assistant configured, which is a supported deployment. */
  assistAvailable: () => request<{ enabled: boolean }>(`${base}/assist`),

  renameRoom: (key: string, newKey: string) =>
    request<RoomDetail>(`${base}/rooms/${key}/rename`, {
      method: 'POST',
      body: JSON.stringify({ newKey }),
    }),

  /**
   * Creates and links a room, or materializes one a dangling exit already names - the server
   * picks from current state, so the caller never has to know which case it is (PLAN.md §7.6).
   */
  dig: (key: string, direction: string, options?: { reciprocal?: boolean; zoneKey?: string }) =>
    request<RoomDetail>(`${base}/rooms/${key}/dig`, {
      method: 'POST',
      body: JSON.stringify({ direction, reciprocal: options?.reciprocal ?? true, zoneKey: options?.zoneKey ?? null }),
    }),

  /**
   * A PUT states the whole exit, so conditions left out are conditions it does not have - which
   * is what lets a lock be taken off again (PLAN.md §4.15).
   */
  setExit: (
    key: string,
    direction: string,
    to: string,
    reciprocal = true,
    conditions?: ExitConditions,
  ) =>
    request<RoomDetail>(`${base}/rooms/${key}/exits/${direction}`, {
      method: 'PUT',
      body: JSON.stringify({
        to,
        reciprocal,
        requiredFlagKey: conditions?.requiredFlagKey ?? null,
        requiredItemKey: conditions?.requiredItemKey ?? null,
        refusalMessage: conditions?.refusalMessage ?? null,
        reciprocalConditions: conditions?.reciprocalConditions ?? false,
      }),
    }),

  removeExit: (key: string, direction: string) =>
    request<RoomDetail>(`${base}/rooms/${key}/exits/${direction}`, { method: 'DELETE' }),

  /**
   * Sets one flag without sending the whole map. `null` clears the key so the zone or world
   * decides again. Narrow by design: a full-object room PATCH replaces every flag, which
   * quietly discards whatever another builder changed in the meantime (PLAN §1).
   */
  setRoomFlag: (key: string, flag: string, value: FlagValue | null) =>
    request<RoomDetail>(`${base}/rooms/${key}/flags/${flag}`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),

  validate: (zoneKey: string) => request<ZoneValidation>(`${base}/zones/${zoneKey}/validate`),

  unfinished: (zoneKey: string) =>
    request<UnfinishedRoom[]>(`${base}/zones/${zoneKey}/unfinished`),

  audit: (kind: string, key: string) =>
    request<AuditEntry[]>(`${base}/audit?kind=${kind}&key=${encodeURIComponent(key)}`),

  abilities: () => request<Ability[]>(`${base}/abilities`),

  ability: (key: string) => request<Ability>(`${base}/abilities/${key}`),

  createAbility: (key: string, body: Partial<Ability>) =>
    request<Ability>(`${base}/abilities/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateAbility: (key: string, body: Partial<Ability>) =>
    request<Ability>(`${base}/abilities/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteAbility: (key: string) =>
    request<void>(`${base}/abilities/${key}`, { method: 'DELETE' }),

  mobTemplates: () => request<MobTemplate[]>(`${base}/mob-templates`),

  mobTemplate: (key: string) => request<MobTemplate>(`${base}/mob-templates/${key}`),

  createMobTemplate: (key: string, body: Partial<MobTemplate>) =>
    request<MobTemplate>(`${base}/mob-templates/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateMobTemplate: (key: string, body: Partial<MobTemplate>) =>
    request<MobTemplate>(`${base}/mob-templates/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteMobTemplate: (key: string) =>
    request<void>(`${base}/mob-templates/${key}`, { method: 'DELETE' }),

  /**
   * Where this mob actually stands in the world (PLAN.md §7.9): the spawners that place it and
   * the rooms they place it into.
   *
   * A spawner names its template and never the other way round, so this is the one direction the
   * relationship cannot be read from the template's own record.
   */
  mobPlacement: (key: string) =>
    request<TemplatePlacement>(`${base}/mob-templates/${key}/placement`),

  itemTemplates: () => request<ItemTemplate[]>(`${base}/item-templates`),

  itemTemplate: (key: string) => request<ItemTemplate>(`${base}/item-templates/${key}`),

  createItemTemplate: (key: string, body: Partial<ItemTemplate>) =>
    request<ItemTemplate>(`${base}/item-templates/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateItemTemplate: (key: string, body: Partial<ItemTemplate>) =>
    request<ItemTemplate>(`${base}/item-templates/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteItemTemplate: (key: string) =>
    request<void>(`${base}/item-templates/${key}`, { method: 'DELETE' }),

  /**
   * Where this item comes from (PLAN.md §7.9): its own spawners and their rooms, the mobs whose
   * loot drops it, the shopkeepers who stock it, and the quests that hand it over or ask for it.
   *
   * @see mobPlacement — same shape, and the three extra lists are why an item needs its own
   * route rather than sharing one: most items have no ground spawner at all.
   */
  itemPlacement: (key: string) =>
    request<TemplatePlacement>(`${base}/item-templates/${key}/placement`),

  spawners: (zoneKey?: string) =>
    request<Spawner[]>(zoneKey ? `${base}/spawners?zone=${zoneKey}` : `${base}/spawners`),

  spawner: (id: string) => request<Spawner>(`${base}/spawners/${id}`),

  createSpawner: (body: SpawnerSave) =>
    request<Spawner>(`${base}/spawners`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateSpawner: (id: string, body: SpawnerSave) =>
    request<Spawner>(`${base}/spawners/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteSpawner: (id: string) =>
    request<void>(`${base}/spawners/${id}`, { method: 'DELETE' }),

  quests: () => request<Quest[]>(`${base}/quests`),

  quest: (key: string) => request<Quest>(`${base}/quests/${key}`),

  createQuest: (key: string, body: Partial<Quest>) =>
    request<Quest>(`${base}/quests/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  updateQuest: (key: string, body: Partial<Quest>) =>
    request<Quest>(`${base}/quests/${key}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),

  deleteQuest: (key: string) =>
    request<void>(`${base}/quests/${key}`, { method: 'DELETE' }),

  questReachability: (key: string) =>
    request<QuestReachability>(`${base}/quests/${key}/reachability`),

  configurations: () => request<GameConfigurationList>(`${base}/configurations`),

  /**
   * What this server refuses to hear. One list for the server, not one per configuration: it is
   * not a property of which world is loaded, and activating a realm must not change it.
   */
  moderation: () => request<ModerationPolicy>(`${base}/moderation`),

  saveModeration: (blockedWords: string) =>
    request<ModerationPolicy>(`${base}/moderation`, {
      method: 'PUT',
      body: JSON.stringify({ blockedWords }),
    }),

  saveConfiguration: (
    key: string,
    body: Pick<
      GameConfiguration,
      'name' | 'description' | 'startingRoomKey' | 'welcomeMessage' | 'canon' | 'worldKeys'
    >,
  ) =>
    request<GameConfiguration>(`${base}/configurations/${key}`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  deleteConfiguration: (key: string) =>
    request<void>(`${base}/configurations/${key}`, { method: 'DELETE' }),

  /** Makes one live. Takes effect for the next character to enter the game, with no restart. */
  activateConfiguration: (key: string) =>
    request<GameConfiguration>(`${base}/configurations/${key}/activate`, { method: 'POST' }),

  /**
   * The whole authored world as one JSON document, or one world, or one zone.
   *
   * A plain URL rather than a fetch: the response carries a Content-Disposition attachment with a
   * dated filename, so letting the browser follow it saves a file somebody keeps. Reading it into
   * a blob here would throw that name away and hand back an untitled download.
   */
  /**
   * Where to download a bundle from.
   *
   * `only: 'abilities'` is the return leg of tuning an ability: they are content, they live in
   * `content/abilities.json`, and a retune made in the editor has to be able to get back to the
   * file. It wins over world and zone rather than narrowing them — an ability belongs to a Path
   * and not to a place.
   */
  exportUrl: (scope?: {
    world?: string
    zone?: string
    only?: 'abilities'
    /** A whole configuration: the worlds it tags, and itself with its canon. Wins over the rest. */
    configuration?: string
  }) => {
    const query = new URLSearchParams()
    if (scope?.configuration) query.set('configuration', scope.configuration)
    else if (scope?.only) query.set('only', scope.only)
    else if (scope?.zone) query.set('zone', scope.zone)
    else if (scope?.world) query.set('world', scope.world)
    const suffix = query.toString()
    return suffix ? `${base}/export?${suffix}` : `${base}/export`
  },

  /**
   * Applies a bundle. `dryRun` reports what would happen and changes nothing.
   *
   * The bundle is sent as already-parsed JSON rather than as text, so a malformed file fails in
   * the browser with a parse error naming the position instead of arriving at the server as a
   * 400 that says only that the body was unreadable.
   */
  importBundle: (bundle: unknown, dryRun: boolean) =>
    request<ImportReport>(`${base}/import${dryRun ? '?dryRun=true' : ''}`, {
      method: 'POST',
      body: JSON.stringify(bundle),
    }),

  storyline: (zoneKey: string) => request<Storyline>(`${base}/zones/${zoneKey}/storyline`),
}

export const DIRECTIONS = ['north', 'east', 'south', 'west', 'up', 'down'] as const

export const OPPOSITE: Record<string, string> = {
  north: 'south',
  south: 'north',
  east: 'west',
  west: 'east',
  up: 'down',
  down: 'up',
}

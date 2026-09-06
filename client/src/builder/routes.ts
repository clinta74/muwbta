/**
 * Pure mapping between the builder's URL and its selection state (PLAN §4).
 *
 * The URL carries slug *segments* - `/builder/world/aldenmoor/millbrook/north-gate/flags` -
 * while the API speaks dotted composite *keys* - `aldenmoor`, `aldenmoor.millbrook`,
 * `aldenmoor.millbrook.north-gate`. Everything here is a pure function so it can be unit
 * tested without a router (the app is `environment: 'node'` under Vitest).
 */

export const SECTIONS = ['details', 'flags', 'terrain', 'exits', 'spawners'] as const
export type Section = (typeof SECTIONS)[number]

/** Rooms open on their prose first; a bare room URL redirects here. */
export const DEFAULT_SECTION: Section = 'details'

/**
 * `accounts` is the odd one out: the other four edit the world and open to any Builder, while
 * this one administers people and is Admin-only. It lives here rather than in a separate app
 * because it is the same audience, the same chrome, and one more screen to build otherwise.
 */
export type BuilderTab =
  | 'world'
  | 'mobs'
  | 'items'
  | 'abilities'
  | 'quests'
  | 'setup'
  | 'accounts'

/**
 * The parts of setup that are not about one entity in a world (PLAN.md §4.16, §6), plus access
 * tokens (docs/PAT-AND-MCP.md §9).
 *
 * Tokens are the odd one: the other two are server-wide and this one is personal. It sits here
 * because Setup is already the builder-visible home for everything that is not a world, and the
 * alternative was opening the Admin-only Accounts tab to builders to gate a single panel.
 */
export const SETUP_SECTIONS = ['configurations', 'transfer', 'tokens'] as const
export type SetupSection = (typeof SETUP_SECTIONS)[number]

/** The route params react-router extracts from the `world` branch, each a single slug. */
export interface WorldRouteParams {
  world?: string
  zone?: string
  room?: string
  section?: string
}

/** The composite keys the selection resolves to, recomposed from the slug segments. */
export interface WorldSelection {
  worldKey: string | null
  zoneKey: string | null
  roomKey: string | null
  section: Section
}

function isSection(value: string | undefined): value is Section {
  return value !== undefined && (SECTIONS as readonly string[]).includes(value)
}

/** The last dotted segment of a composite key - its own slug. */
function slug(key: string): string {
  return key.slice(key.lastIndexOf('.') + 1)
}

/**
 * Recomposes composite keys from slug segments. A zone is only meaningful with its world,
 * and a room only with both, so a missing parent collapses everything below it to null -
 * which is exactly the "nothing selected yet" state the tree starts in.
 */
export function keysFromParams(params: WorldRouteParams): WorldSelection {
  const worldKey = params.world ?? null
  const zoneKey = worldKey && params.zone ? `${worldKey}.${params.zone}` : null
  const roomKey = zoneKey && params.room ? `${zoneKey}.${params.room}` : null

  return {
    worldKey,
    zoneKey,
    roomKey,
    // An unknown or absent section is not an error - it just means "the default one".
    section: isSection(params.section) ? params.section : DEFAULT_SECTION,
  }
}

/**
 * Builds the path for a world-tab selection. Deeper arguments are ignored once a shallower
 * one is null, so `toWorldPath('w', null, 'w.z.r')` is just `/builder/world/w` - you cannot
 * address a room without its zone.
 */
export function toWorldPath(
  worldKey: string | null,
  zoneKey?: string | null,
  roomKey?: string | null,
  section: Section = DEFAULT_SECTION,
): string {
  if (!worldKey) {
    return '/builder/world'
  }

  const parts = ['/builder/world', worldKey]

  if (zoneKey) {
    parts.push(slug(zoneKey))

    if (roomKey) {
      parts.push(slug(roomKey), section)
    }
  }

  return parts.join('/')
}

/**
 * The path for one room, addressed by its composite key alone.
 *
 * <b>Because most callers hold a room and nothing else.</b> `toWorldPath` wants all three keys,
 * which is right when the caller is a tree that already knows where it is and wrong everywhere
 * else — a spawner, a quest, a placement row all name a room and nothing above it. Splitting the
 * key at each call site is the duplication this avoids, and the split is not quite obvious: a
 * room key is `world.zone.room`, so the zone is the first *two* segments and not the second.
 */
export function toRoomPath(roomKey: string, section: Section = DEFAULT_SECTION): string {
  const parts = roomKey.split('.')

  if (parts.length < 3) {
    return toWorldPath(parts[0] ?? null, parts.length > 1 ? roomKey : null)
  }

  return toWorldPath(parts[0], `${parts[0]}.${parts[1]}`, roomKey, section)
}

export function toMobsPath(templateKey?: string | null): string {
  return templateKey ? `/builder/mobs/${templateKey}` : '/builder/mobs'
}

export function toItemsPath(templateKey?: string | null): string {
  return templateKey ? `/builder/items/${templateKey}` : '/builder/items'
}

export function toAbilitiesPath(abilityKey?: string | null): string {
  return abilityKey ? `/builder/abilities/${abilityKey}` : '/builder/abilities'
}

export function toQuestsPath(questKey?: string | null): string {
  return questKey ? `/builder/quests/${questKey}` : '/builder/quests'
}

export function toAccountsPath(username?: string | null): string {
  return username ? `/builder/accounts/${encodeURIComponent(username)}` : '/builder/accounts'
}

export function toSetupPath(section?: SetupSection | null): string {
  return section ? `/builder/setup/${section}` : '/builder/setup'
}

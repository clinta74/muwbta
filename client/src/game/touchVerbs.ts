/**
 * What the exit pad and the command chips show. Both are derived entirely from state the client
 * already holds — the room's exits and the character's own history — so neither costs a protocol
 * change (MOBILE.md M2).
 */

export interface PadKey {
  /** The command sent on tap. */
  direction: string
  /** What the key reads as. Single letters, because six of these share one row. */
  label: string
  /** False when the room has no such exit. Shown anyway, disabled — see `exitPad`. */
  available: boolean
}

/**
 * The six directions, in the order they appear on the pad.
 *
 * Compass first and left to right, then up and down. Not the north-south-east-west order the
 * server lists exits in: this is a row of keys under a thumb, and the thumb wants the two vertical
 * ones together at the end where they will not be hit by accident.
 */
const DIRECTIONS: ReadonlyArray<{ direction: string; label: string }> = [
  { direction: 'north', label: 'N' },
  { direction: 'east', label: 'E' },
  { direction: 'south', label: 'S' },
  { direction: 'west', label: 'W' },
  { direction: 'up', label: '↑' },
  { direction: 'down', label: '↓' },
]

/**
 * Every direction, marked with whether this room has it.
 *
 * **All six, always.** Rendering only the exits that exist would reflow the row on every move, so
 * the key under your thumb would be a different direction each time you arrived somewhere — which
 * is precisely how you walk back into the room you just left. A disabled key holds its place.
 */
export function exitPad(exits: readonly string[]): PadKey[] {
  // The server sends full names; compare on lowercase so casing on the wire cannot matter.
  const present = new Set(exits.map((exit) => exit.toLowerCase()))

  return DIRECTIONS.map(({ direction, label }) => ({
    direction,
    label,
    available: present.has(direction),
  }))
}

/**
 * The most recent distinct commands, newest first, for the chip row above the input.
 *
 * Distinct because history keeps every `north` you typed and a row of six identical chips helps
 * nobody. Newest first because the thumb starts at the left and the last thing you did is the
 * thing most likely to be next.
 *
 * Movement is filtered out: the exit pad is directly below and does it better, so spending chips
 * on directions would leave no room for the commands that are genuinely awkward to retype.
 */
export function recentCommands(history: readonly string[], limit = 4): string[] {
  const movement = new Set([
    ...DIRECTIONS.map((d) => d.direction),
    'n', 'e', 's', 'w', 'u', 'd',
  ])

  const seen = new Set<string>()
  const recent: string[] = []

  for (let i = history.length - 1; i >= 0 && recent.length < limit; i--) {
    const command = history[i].trim()
    const key = command.toLowerCase()

    if (!command || seen.has(key) || movement.has(key)) continue

    seen.add(key)
    recent.push(command)
  }

  return recent
}

export interface Verb {
  label: string
  /** The command, with the target's keyword already in it. */
  command: string
}

/**
 * What a tap on somebody in the room offers.
 *
 * On a desktop, clicking a name types its keyword into the input and the player finishes the
 * sentence — which is a good interaction when a keyboard is one key away and a poor one when it
 * costs summoning a keyboard over the game. So on touch the same tap offers the verbs instead.
 *
 * The last entry is the escape hatch: it does exactly what the desktop click does, so nothing the
 * player could do before is now unreachable.
 */
export function verbsFor(keyword: string): Verb[] {
  return [
    { label: 'Look at', command: `look ${keyword}` },
    { label: 'Attack', command: `attack ${keyword}` },
    { label: 'Talk to', command: `talk ${keyword}` },
    { label: 'Get', command: `get ${keyword}` },
  ]
}

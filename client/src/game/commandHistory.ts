/**
 * Command history that survives a reload.
 *
 * It lived in component state, which was enough to survive opening the builder - the game is
 * hidden rather than unmounted - but not a refresh. Losing it there is worse than it sounds,
 * because a refresh is what a player reaches for when the stream looks stuck, and it took the
 * last hour of typing with it.
 *
 * Keyed by character rather than by account: two characters have different things in reach, and
 * arrowing back through the other one's commands would mostly produce things that no longer work.
 */

const CAP = 100

/** Storage can throw outright in private browsing, and history is never worth a broken input. */
function storage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

function keyFor(characterId: string): string {
  return `muwbta.history.${characterId}`
}

export function loadHistory(characterId: string): string[] {
  try {
    const raw = storage()?.getItem(keyFor(characterId))
    if (!raw) return []

    const parsed: unknown = JSON.parse(raw)

    // Anything could be under that key - an older shape, or another tab mid-write. A malformed
    // entry costs nothing to drop and would otherwise render as a blank line in the input.
    return Array.isArray(parsed) ? parsed.filter((e) => typeof e === 'string').slice(-CAP) : []
  } catch {
    return []
  }
}

export function saveHistory(characterId: string, history: readonly string[]): void {
  try {
    storage()?.setItem(keyFor(characterId), JSON.stringify(history.slice(-CAP)))
  } catch {
    // A full quota is not worth interrupting a command for.
  }
}

/**
 * The history with one more command on the end.
 *
 * An immediate repeat is not recorded twice, because the value of arrowing up is reaching the
 * command *before* the one just typed. Ten `north`s in a row would otherwise bury it.
 */
export function remember(history: readonly string[], input: string): string[] {
  if (history[history.length - 1] === input) return [...history]

  return [...history, input].slice(-CAP)
}

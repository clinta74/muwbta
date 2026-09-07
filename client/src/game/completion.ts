/**
 * Tab completion for the argument half of a command.
 *
 * Only the argument half. Verbs are not completed and there is no verb list here on purpose: the
 * engine already prefix-matches them (`CommandDefinition.Matches`), so `ex` reaches examine and
 * `n` reaches north without any help from the client. Shipping a copy of the verb table to the
 * browser would add a second list to keep in step with the first, to save keystrokes the server
 * already saves.
 *
 * Names are the opposite case. They are authored prose - "a drowned rat", "the empty glass" - and
 * the player has to reproduce enough of one to be unambiguous. That is where the typing actually
 * goes, and it is what the room already tells the client about.
 */

/** Where a completion would be spliced in, and what could go there. */
export interface Completions {
  /** Index into the input where the replaced fragment begins. */
  readonly from: number
  /** Best first, deduplicated. Empty when nothing fits. */
  readonly matches: readonly string[]
}

const NONE: Completions = { from: 0, matches: [] }

/**
 * What the trailing fragment of <paramref name="value"/> could be completed to.
 *
 * The fragment is found rather than assumed, because names run to several words and there is no
 * marker saying where one starts. Every word boundary after the verb is tried as a starting point,
 * longest fragment first, and the first one that matches anything wins. So in
 * <c>give empty gl</c> the fragment is "empty gl" rather than "gl" - which matters, because "gl"
 * alone would also have matched a glass lantern lying on the floor.
 */
export function completionsFor(value: string, candidates: readonly string[]): Completions {
  for (const from of fragmentStarts(value)) {
    const fragment = value.slice(from)
    const matches = rank(fragment, candidates)

    if (matches.length > 0) return { from, matches }
  }

  return NONE
}

/** The input with a completion spliced in. */
export function applyCompletion(value: string, completions: Completions, index: number): string {
  const match = completions.matches[index]
  return match === undefined ? value : value.slice(0, completions.from) + match
}

/**
 * Word starts, longest fragment first, excluding the verb.
 *
 * Excluding the verb is what keeps `loo` from being completed into the name of something in the
 * room. A trailing space counts as a start of its own, so Tab on `give ` offers everything here -
 * which is the closest thing this has to "what can I even target".
 */
function fragmentStarts(value: string): number[] {
  const starts: number[] = []

  for (const match of value.matchAll(/\S+/g)) {
    if (match.index > 0) starts.push(match.index)
  }

  // Only after at least one word, so an empty input completes to nothing rather than to
  // everything.
  if (/\S\s+$/.test(value)) starts.push(value.length)

  return starts
}

/**
 * Candidates that the fragment could be growing into, best first.
 *
 * The tiers echo the shape of `NameMatch.RankOf` on the server: the whole name first, then the
 * last word - the noun, in an English noun phrase - then any word.
 *
 * They are an echo rather than a copy, and cannot be more than that: the engine also ranks against
 * each thing's template key, which the client is never sent, so the two orders can disagree. That
 * is affordable here in a way it would not be in targeting code, because completing replaces the
 * fragment with the *whole* name, which the engine ranks as an exact match. Getting the order
 * wrong costs an extra press of Tab. It cannot cost the wrong target.
 */
function rank(fragment: string, candidates: readonly string[]): string[] {
  const needle = fragment.toLowerCase()
  const tiers: string[][] = [[], [], []]

  for (const candidate of candidates) {
    const name = candidate.toLowerCase()

    if (name.startsWith(needle)) {
      tiers[0].push(candidate)
      continue
    }

    const words = name.split(/[\s-_]+/).filter(Boolean)
    if (words.length > 1 && words[words.length - 1].startsWith(needle)) {
      tiers[1].push(candidate)
      continue
    }

    if (words.some((word) => word.startsWith(needle))) {
      tiers[2].push(candidate)
    }
  }

  const seen = new Set<string>()
  const ordered: string[] = []

  for (const candidate of tiers.flat()) {
    const key = candidate.toLowerCase()
    if (seen.has(key)) continue

    seen.add(key)
    ordered.push(candidate)
  }

  return ordered
}

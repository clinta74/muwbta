import { describe, expect, it } from 'vitest'
import { applyCompletion, completionsFor } from './completion'

/** What a tavern with two similar things in it offers. */
const ROOM = ['a bar maiden', 'an old man', 'an empty glass', 'a glass lantern', 'Mira']

function complete(value: string, candidates = ROOM, index = 0): string {
  const completions = completionsFor(value, candidates)
  return applyCompletion(value, completions, index)
}

describe('growing the trailing name', () => {
  it('completes a one-word fragment', () => {
    expect(complete('kill Mi')).toBe('kill Mira')
  })

  it('completes across the words of a name', () => {
    // The whole reason the fragment is searched for rather than taken as the last word.
    expect(complete('give an empty gl')).toBe('give an empty glass')
  })

  it('prefers the longest fragment that matches anything', () => {
    // "gl" on its own also matches the lantern. Anchoring on "empty gl" is what rules it out,
    // and it is the difference between one candidate and a cycle of two.
    expect(completionsFor('give an empty gl', ROOM).matches).toEqual(['an empty glass'])
  })

  it('finds a name by a later word, the way the engine targets one', () => {
    // NameMatch ranks the last word of a noun phrase highest, because that is the noun. A player
    // types "maiden", never "a bar".
    expect(complete('talk maid')).toBe('talk a bar maiden')
  })

  it('offers everything after a trailing space', () => {
    // The closest thing this has to "what can I target".
    expect(completionsFor('give ', ROOM).matches).toEqual(ROOM)
  })

  it('splices in place rather than appending', () => {
    const value = 'give an empty gl'
    const completions = completionsFor(value, ROOM)

    expect(completions.from).toBe(5)
    expect(applyCompletion(value, completions, 0)).toBe('give an empty glass')
  })
})

describe('what it refuses to complete', () => {
  it('leaves the verb alone', () => {
    // No verb list ships to the client, deliberately: the engine already prefix-matches verbs.
    // Without this rule "ma" in the verb position would become "a bar maiden".
    expect(completionsFor('ma', ROOM).matches).toEqual([])
    expect(completionsFor('', ROOM).matches).toEqual([])
  })

  it('offers nothing when the fragment matches nothing', () => {
    // The caller reads an empty list as "let Tab move focus instead".
    expect(completionsFor('kill zzz', ROOM).matches).toEqual([])
  })

  it('offers nothing in an empty room', () => {
    expect(completionsFor('kill r', []).matches).toEqual([])
  })
})

describe('several things answer to the same word', () => {
  it('offers each of them, the noun before the adjective', () => {
    // Neither name begins with "gl", so the tiebreak is the last word - the noun of the phrase.
    // The glass is a glass; the lantern is a lantern that happens to be made of one.
    //
    // This order is not the engine's. NameMatch also ranks against the template key, so `get gl`
    // typed and sent would reach the lantern on its "glass-lantern" prefix. That divergence is
    // survivable in a way it would not be for targeting, because completing replaces the fragment
    // with the whole name - which is an exact match, and the best rank there is. The order only
    // decides how many times Tab is pressed, never what gets hit.
    expect(completionsFor('get gl', ROOM).matches).toEqual(['an empty glass', 'a glass lantern'])
  })

  it('cycles by index, and the caller wraps', () => {
    expect(complete('get gl', ROOM, 0)).toBe('get an empty glass')
    expect(complete('get gl', ROOM, 1)).toBe('get a glass lantern')
  })

  it('lists a duplicated name once', () => {
    // Two rats in a room are two entries in the contents frame and one thing to type.
    expect(completionsFor('kill ra', ['a rat', 'a rat', 'A Rat']).matches).toEqual(['a rat'])
  })
})

describe('case', () => {
  it('matches without regard to it but completes to the authored spelling', () => {
    expect(complete('kill mira')).toBe('kill Mira')
  })
})

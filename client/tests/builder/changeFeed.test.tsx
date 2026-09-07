// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, waitFor } from '@testing-library/react'
import { BuilderDataProvider } from '@/builder/BuilderData'

/**
 * The builder change feed coalesces.
 *
 * The server emits one `entity-changed` event per changed entity, and every branch of the handler
 * refetches a whole collection. That is the right trade for somebody editing one room at a time
 * and the wrong one for a bulk change: importing the world moves 224 rooms, 100 spawners, 93
 * items, 69 mobs and 35 quests, so the feed asked for the full quest list thirty-five times and
 * the full room list three hundred — eight hundred-odd requests against a rate limit of 120
 * refilling at 20 a second, which is a wall of 429s and a builder that fails to update.
 *
 * Deduplication is the whole mechanism: thirty-five quest events and one are the same request,
 * because the request was never about *which* quest changed.
 */

const counts = vi.hoisted(() => ({ quests: 0, mobs: 0, items: 0 }))

vi.mock('@/net/builderApi', () => ({
  builderApi: {
    roomFlags: () => Promise.resolve([]),
    worlds: () => Promise.resolve([]),
    zones: () => Promise.resolve([]),
    rooms: () => Promise.resolve([]),
    validate: () => Promise.resolve(null),
    mobTemplates: () => {
      counts.mobs += 1
      return Promise.resolve([])
    },
    itemTemplates: () => {
      counts.items += 1
      return Promise.resolve([])
    },
    quests: () => {
      counts.quests += 1
      return Promise.resolve([])
    },
  },
}))

/** Captures the handler the provider registers, so a test can drive the feed by hand. */
class FakeEventSource {
  static last: FakeEventSource | null = null

  private handlers: ((event: MessageEvent) => void)[] = []

  closed = false

  // Declared and assigned rather than written as a parameter property. `erasableSyntaxOnly` is on
  // in tsconfig, and a parameter property is the one piece of TypeScript here that emits runtime
  // code rather than being erased - so it fails the build (TS1294) while looking like ordinary
  // type annotation. This spelling is the same thing without the codegen.
  url: string

  constructor(url: string) {
    this.url = url
    FakeEventSource.last = this
  }

  addEventListener(_name: string, handler: EventListener) {
    this.handlers.push(handler as (event: MessageEvent) => void)
  }

  close() {
    this.closed = true
  }

  /** Delivers one `entity-changed` payload, the way the server writes it. */
  emit(kind: string, key: string) {
    const event = { data: JSON.stringify({ kind, key, action: 'upsert' }) } as MessageEvent
    for (const handler of this.handlers) handler(event)
  }
}

describe('the builder change feed', () => {
  beforeEach(() => {
    counts.quests = 0
    counts.mobs = 0
    counts.items = 0
    FakeEventSource.last = null
    vi.stubGlobal('EventSource', FakeEventSource)
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  /** Mounted and past its one-time loads, with the feed ready to drive. */
  async function mounted() {
    render(<BuilderDataProvider>{null}</BuilderDataProvider>)

    await waitFor(() => expect(counts.quests).toBe(1))
    expect(FakeEventSource.last).not.toBeNull()

    return FakeEventSource.last!
  }

  it('asks once for a burst of events about the same collection', async () => {
    const feed = await mounted()

    // What an import of the shipped content does.
    for (let i = 0; i < 35; i++) feed.emit('quest', `a3-${i}`)

    await waitFor(() => expect(counts.quests).toBe(2), { timeout: 2000 })

    // Held for a further quiet period, so this is the settled answer rather than a race the
    // assertion happened to win.
    await new Promise((resolve) => setTimeout(resolve, 600))
    expect(counts.quests).toBe(2)
  })

  it('still refreshes for a single edit by another builder', async () => {
    const feed = await mounted()

    feed.emit('quest', 'a3-1-blanks-and-cogs')

    await waitFor(() => expect(counts.quests).toBe(2), { timeout: 2000 })
  })

  /**
   * Coalescing is per collection, not global — an import touching several kinds must still end
   * with every one of them reloaded, or the builder shows stale mobs beside fresh quests.
   */
  it('reloads every kind that changed, once each', async () => {
    const feed = await mounted()

    for (let i = 0; i < 20; i++) {
      feed.emit('quest', `q${i}`)
      feed.emit('mob-template', `m${i}`)
      feed.emit('item-template', `i${i}`)
    }

    await waitFor(
      () => {
        expect(counts.quests).toBe(2)
        expect(counts.mobs).toBe(2)
        expect(counts.items).toBe(2)
      },
      { timeout: 2000 },
    )
  })

  it('closes the stream when the provider goes away', async () => {
    const feed = await mounted()

    cleanup()

    expect(feed.closed).toBe(true)
  })
})

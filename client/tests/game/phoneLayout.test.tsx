// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { GameScreen } from '@/game/GameScreen'
import type { GameEvent } from '@/net/protocol'

const sent: string[] = []
const entered: string[] = []

vi.mock('@/net/api', () => ({
  api: {
    command: (_id: string, input: string) => {
      sent.push(input)
      return Promise.resolve()
    },
    enter: (id: string) => {
      entered.push(id)
      return Promise.resolve({})
    },
  },
}))

const stream = vi.hoisted(() => ({
  emit: null as ((event: GameEvent) => void) | null,
  fail: null as (() => void) | null,
  open: null as (() => void) | null,
}))

vi.mock('@/net/stream', () => ({
  connectStream: (
    _id: string,
    handlers: {
      onEvent: (event: GameEvent) => void
      onError?: () => void
      onOpen?: () => void
    },
  ) => {
    stream.emit = handlers.onEvent
    stream.fail = () => handlers.onError?.()
    stream.open = () => handlers.onOpen?.()
    return () => {
      stream.emit = null
      stream.fail = null
      stream.open = null
    }
  },
}))

/**
 * jsdom implements no media queries at all, so the layout has to be told which one it is in. Every
 * query is answered from one map — a test that says "phone" gets a phone for both the width
 * question and the pointer question, which is what a phone actually is.
 */
function pretendToBe(kind: 'phone' | 'desktop') {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: kind === 'phone',
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

/**
 * What Radix needs and jsdom does not have.
 *
 * The session menu is a Radix dropdown, which measures its trigger to place the panel and takes
 * pointer capture to track the press. jsdom implements neither, and the failure is not an error -
 * the menu simply never opens, so a test asserting on its contents reads as the menu being empty
 * rather than as the environment being incomplete.
 */
function stubTheDomRadixExpects() {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )

  Element.prototype.hasPointerCapture ??= () => false
  Element.prototype.setPointerCapture ??= () => {}
  Element.prototype.releasePointerCapture ??= () => {}
  Element.prototype.scrollIntoView ??= () => {}
}

/**
 * Radix opens on `pointerdown`, not on `click` - it commits to the menu when the finger lands
 * rather than when it lifts, which is what makes a press-and-drag onto an item work on a phone.
 * `fireEvent.click` therefore does nothing at all here.
 */
async function openTheMenu() {
  const trigger = screen.getByRole('button', { name: /character and session/i })

  await act(async () => {
    fireEvent.pointerDown(trigger, { button: 0, ctrlKey: false, pointerType: 'mouse' })
  })
}

beforeEach(() => {
  sent.length = 0
  entered.length = 0
  localStorage.clear()
  stubTheDomRadixExpects()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function play() {
  return render(
    <GameScreen characterId="c1" characterName="Kael" onLeave={() => {}} active />,
  )
}

function sheet(container: HTMLElement) {
  return container.querySelector('.room-sheet') as HTMLElement
}

/** The exits only reach the component over the stream, so a test that wants a pad has to arrive. */
function arriveIn(exits: string[]) {
  act(() =>
    stream.emit?.({
      type: 'room',
      data: { key: 'aldenmoor.millbrook.north-gate', title: 'The North Gate', description: '', exits },
    }),
  )
}

it('starts with the room sheet closed, so the transcript has the screen', () => {
  // The whole point of the layout: the map and the room description are reference material you
  // consult, not the thing you watch. Opening on top of the transcript would undo it.
  pretendToBe('phone')
  const { container } = play()

  expect(container.querySelector('.game')?.getAttribute('data-layout')).toBe('phone')
  expect(sheet(container).dataset.open).toBe('false')
  expect(sheet(container).getAttribute('aria-hidden')).toBe('true')
})

it('opens and closes the sheet from the header', () => {
  pretendToBe('phone')
  const { container } = play()

  fireEvent.click(screen.getByRole('button', { name: /room/i }))
  expect(sheet(container).dataset.open).toBe('true')
  expect(sheet(container).getAttribute('aria-hidden')).toBe('false')

  fireEvent.click(screen.getByRole('button', { name: /close/i }))
  expect(sheet(container).dataset.open).toBe('false')
})

it('closes the sheet on Escape', () => {
  pretendToBe('phone')
  const { container } = play()

  fireEvent.click(screen.getByRole('button', { name: /room/i }))
  fireEvent.keyDown(document, { key: 'Escape' })

  expect(sheet(container).dataset.open).toBe('false')
})

it('leaves the panels visible and unhidden on a desktop', () => {
  // The sheet wrapper exists in both layouts — one tree, two shapes — so the guard that matters is
  // that its ARIA state does not follow the phone's. Hiding panels that are plainly on screen from
  // assistive tech would be a lie the sighted user cannot see.
  pretendToBe('desktop')
  const { container } = play()

  expect(container.querySelector('.game')?.getAttribute('data-layout')).toBe('desktop')
  expect(sheet(container).getAttribute('aria-hidden')).toBe('false')
})

/**
 * What the bottom row costs, and why it is now one line.
 *
 * The phone grid is header / scroll / notice / pad / cooldowns / input / vitals, and `scroll` is
 * the only row that flexes - so every pixel any other row spends is taken from the transcript.
 * When the on-screen keyboard opens, `--keyboard-inset` shrinks the whole grid and `scroll`
 * absorbs all of that too, on top of what the other rows were already holding. Identity was two
 * wrapped lines and the buttons a 44px row: about eighty pixels out of the two or three hundred
 * left to read in.
 */
const vitals = () =>
  act(() =>
    stream.emit?.({
      type: 'vitals',
      data: {
        health: 40, healthMax: 60,
        focus: 10, focusMax: 20,
        stamina: 30, staminaMax: 30,
        level: 12, path: 'Warden', xp: 3400, gold: 275,
      },
    }),
  )

it('keeps only the meters in the bottom row on a phone', () => {
  pretendToBe('phone')
  const { container } = play()
  vitals()

  const bar = container.querySelector('.vitals-bar') as HTMLElement

  // The three that change while you are looking at them stay.
  expect(bar.querySelectorAll('.meter')).toHaveLength(3)

  // What does not change while you read it goes to the header menu.
  expect(bar.querySelector('.identity')).toBeNull()
  expect(bar.textContent).not.toContain('gold')
  expect(bar.querySelector('button')).toBeNull()
})

it('still puts everything in the bottom row on a desktop', () => {
  // The row is only a problem where the keyboard takes half the screen. On a desktop it has the
  // width for all of it and a menu would be a tap where there was none.
  pretendToBe('desktop')
  const { container } = play()
  vitals()

  const bar = container.querySelector('.vitals-bar') as HTMLElement

  expect(bar.querySelector('.identity')).not.toBeNull()
  expect(bar.textContent).toContain('gold')
  expect(screen.getByRole('button', { name: 'leave' })).toBeTruthy()
  expect(screen.queryByRole('button', { name: /character and session/i })).toBeNull()
})

it('reaches who you are and the way out from the header menu', async () => {
  pretendToBe('phone')
  play()
  vitals()

  await openTheMenu()

  // The identity readout is the reason the menu exists, so it is a label rather than an item -
  // there to be read, not chosen.
  expect(screen.getByText(/Kael · Warden · level 12/)).toBeTruthy()
  expect(screen.getByText(/275 gold/)).toBeTruthy()

  expect(screen.getByRole('menuitem', { name: 'Leave the world' })).toBeTruthy()
})

it('leaves the world from the menu', async () => {
  pretendToBe('phone')
  const left: number[] = []

  render(
    <GameScreen characterId="c1" characterName="Kael" onLeave={() => left.push(1)} active />,
  )
  vitals()

  await openTheMenu()

  await act(async () => {
    fireEvent.click(screen.getByRole('menuitem', { name: 'Leave the world' }))
  })

  expect(left).toHaveLength(1)
})

it('offers the builder in the menu only to an account that has one', async () => {
  // Same rule the bottom row followed: the control is absent, not disabled, for a player who
  // cannot use it.
  pretendToBe('phone')
  render(<GameScreen characterId="c1" characterName="Kael" onLeave={() => {}} active />)
  vitals()

  await openTheMenu()

  expect(screen.queryByRole('menuitem', { name: 'Builder' })).toBeNull()
})

/**
 * A visual viewport that can be made to shrink, the way an on-screen keyboard shrinks a real one.
 *
 * jsdom has none at all, so the whole mechanism is invisible to a test unless one is supplied. It
 * starts uncovered on purpose: the component syncs on mount, so a stub that arrives already
 * covered would put the layout in its keyboard state before the test had done anything.
 */
function fakeViewport() {
  const listeners: (() => void)[] = []
  const viewport = {
    height: 800,
    offsetTop: 0,
    addEventListener: (_: string, fn: () => void) => listeners.push(fn),
    removeEventListener: () => {},
  }

  vi.stubGlobal('visualViewport', viewport)
  Object.defineProperty(window, 'innerHeight', { value: 800, configurable: true })

  return (covered: number) =>
    act(() => {
      viewport.height = 800 - covered
      listeners.forEach((fn) => fn())
    })
}

it('takes the shortcut rows away while the keyboard is up', () => {
  // The pad and the chips are both ways of avoiding the keyboard, so once it is up they are two
  // rows of shortcut to something you are already doing - taken out of a transcript that has just
  // lost half its height to the keyboard itself.
  pretendToBe('phone')
  const cover = fakeViewport()
  const { container } = play()
  arriveIn(['north', 'east'])

  const game = container.querySelector('.game') as HTMLElement
  expect(game.dataset.keyboard).toBe('closed')

  cover(400)

  expect(game.dataset.keyboard).toBe('open')
})

it('leaves the shortcut rows alone for a disagreement of a pixel or two', () => {
  // The two viewports differ by a hair all the time on real hardware. Only a real keyboard is
  // worth reflowing the grid for, and a pad that flickered on scroll would be worse than one that
  // never moved.
  pretendToBe('phone')
  const cover = fakeViewport()
  const { container } = play()

  cover(12)

  expect((container.querySelector('.game') as HTMLElement).dataset.keyboard).toBe('closed')
})

it('narrows the bars on a phone so three of them fit one line', () => {
  // The bar is literal block characters, so its width is a character count and no CSS will shrink
  // it. At ten cells the three could not share a folding Pixel's width - HP and FO took the first
  // line and ST wrapped to a second, and each meter is two rows to begin with.
  pretendToBe('phone')
  const { container } = play()
  vitals()

  for (const bar of container.querySelectorAll('.vitals-self .meter-bar')) {
    expect(bar.textContent).toHaveLength(6)
  }
})

it('keeps the full-width bars on a desktop', () => {
  pretendToBe('desktop')
  const { container } = play()
  vitals()

  for (const bar of container.querySelectorAll('.vitals-self .meter-bar')) {
    expect(bar.textContent).toHaveLength(10)
  }
})

it('still reads the right proportion off a narrowed bar', () => {
  // Fewer cells is a coarser bar, not a wrong one. Two thirds of six is four filled.
  pretendToBe('phone')
  const { container } = play()

  act(() =>
    stream.emit?.({
      type: 'vitals',
      data: {
        health: 40, healthMax: 60,
        focus: 20, focusMax: 20,
        stamina: 0, staminaMax: 30,
        level: 12, path: 'Warden', xp: 3400, gold: 275,
      },
    }),
  )

  const bars = [...container.querySelectorAll('.vitals-self .meter-bar')]
  const filled = (i: number) => [...(bars[i].textContent ?? '')].filter((c) => c === '█').length

  expect(filled(0)).toBe(4)
  expect(filled(1)).toBe(6)
  expect(filled(2)).toBe(0)
})

it('walks with a tap, and keeps the missing directions on the pad', () => {
  // The reason M2 exists: the main verb of a MUD is walking, and walking used to mean typing
  // `north` on a keyboard covering half the screen.
  pretendToBe('phone')
  play()

  arriveIn(['north', 'east'])

  fireEvent.click(screen.getByRole('button', { name: 'north' }))
  expect(sent).toEqual(['north'])

  // Present but disabled, rather than absent: a pad that only drew real exits would reflow on
  // every arrival, moving the key out from under the thumb.
  const west = screen.getByRole('button', { name: 'west' }) as HTMLButtonElement
  expect(west.disabled).toBe(true)

  fireEvent.click(west)
  expect(sent).toEqual(['north'])
})

it('has no exit pad on a desktop', () => {
  pretendToBe('desktop')
  play()
  arriveIn(['north'])

  expect(screen.queryByRole('group', { name: 'Exits' })).toBeNull()
})

it('re-runs a command from a chip without opening the keyboard', () => {
  // The phone's up arrow. Tapping runs it rather than loading it into the box, because sending it
  // from there would mean summoning a keyboard to press a return key.
  pretendToBe('phone')
  const input = play().container.querySelector('input') as HTMLInputElement

  fireEvent.change(input, { target: { value: 'attack wolf' } })
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(sent).toEqual(['attack wolf'])

  fireEvent.click(screen.getByRole('button', { name: 'attack wolf' }))
  expect(sent).toEqual(['attack wolf', 'attack wolf'])
})

it('says nothing until a dropped stream has had a moment to come back', () => {
  // The delay is the whole behaviour being pinned. The stream retries on its own and usually wins,
  // so announcing every blip would make an ordinary page load flash "Disconnected" before the
  // connection had a chance to happen.
  //
  // This used to assert a "Rejoin the world" button, which is gone: the character list already
  // walks somebody back into the world in one click, and it is the way back from being displaced
  // by another device too. Two routes to one place is two things to keep working, and this was the
  // one that had quietly stopped working.
  vi.useFakeTimers()
  try {
    pretendToBe('phone')
    play()

    act(() => stream.fail?.())
    expect(screen.queryByText(/Disconnected/i)).toBeNull()

    act(() => vi.advanceTimersByTime(2500))
    expect(screen.queryByText(/Disconnected/i)).not.toBeNull()
  } finally {
    vi.useRealTimers()
  }
})

it('takes the reconnect bar away as soon as the stream is back', () => {
  vi.useFakeTimers()
  try {
    pretendToBe('phone')
    play()

    act(() => stream.fail?.())
    act(() => vi.advanceTimersByTime(2500))
    expect(screen.queryByText(/Disconnected/i)).not.toBeNull()

    act(() => stream.open?.())
    expect(screen.queryByText(/Disconnected/i)).toBeNull()
  } finally {
    vi.useRealTimers()
  }
})

it('does not steal keystrokes aimed elsewhere on a touch device', () => {
  // Type-anywhere is a desktop convenience. On a phone, focusing the input summons the keyboard
  // over the game, so a stray keydown must not do it.
  pretendToBe('phone')
  play()

  const input = screen.getByLabelText('Command input')
  ;(document.body as HTMLElement).focus()
  fireEvent.keyDown(document, { key: 'k' })

  expect(document.activeElement).not.toBe(input)
})

// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { GameScreen } from './GameScreen'
import type { ContentEntry, GameEvent, PartyMemberEntry } from '../net/protocol'

const sent: string[] = []

vi.mock('../net/api', () => ({
  api: {
    command: (_id: string, input: string) => {
      sent.push(input)
      return Promise.resolve()
    },
    // Nothing in this component reaches this any more - the Rejoin button was the only caller,
    // and getting back into the world is the character list's job now. Kept on the mock because
    // the module needs the shape, not because a test drives it.
    enter: () => Promise.resolve({}),
  },
}))

// Holds the stream's own callback so a test can push frames in, which is the only way the room's
// contents - and therefore the completion candidates - ever reach the component.
const stream = vi.hoisted(() => ({
  emit: null as ((event: GameEvent) => void) | null,
  displace: null as ((message: string) => void) | null,

  // Counted, because reopening the stream is not a harmless extra render: each teardown tells the
  // server the connection dropped, which marks the character link-dead and closes its channel.
  opened: 0,
}))

vi.mock('../net/stream', () => ({
  connectStream: (
    _id: string,
    handlers: {
      onEvent: (event: GameEvent) => void
      onDisplaced?: (message: string) => void
    },
  ) => {
    stream.opened += 1
    stream.emit = handlers.onEvent
    stream.displace = (message: string) => handlers.onDisplaced?.(message)
    return () => {
      stream.emit = null
      stream.displace = null
    }
  },
}))

// jsdom has no layout, so it implements no scrolling: scrollHeight and clientHeight are always 0
// and scrollTop never moves. Every case below that cares about position says so explicitly.
Element.prototype.scrollIntoView = () => {}

beforeEach(() => {
  sent.length = 0
  stream.opened = 0
  localStorage.clear()
})

afterEach(cleanup)

function play({
  active = true,
  characterId = 'c1',
  onLeave = () => {},
  onDisplaced,
}: {
  active?: boolean
  characterId?: string
  onLeave?: () => void
  onDisplaced?: (message: string) => void
} = {}) {
  render(
    <GameScreen
      characterId={characterId}
      characterName="Kael"
      onLeave={onLeave}
      onDisplaced={onDisplaced}
      active={active}
    />,
  )

  return screen.getByLabelText('Command input') as HTMLInputElement
}

function emit(event: GameEvent) {
  act(() => stream.emit?.(event))
}

function inTheRoom(occupants: ContentEntry[], items: ContentEntry[] = []) {
  emit({ type: 'contents', data: { occupants, items } })
}

function entry(label: string): ContentEntry {
  return { icon: '@', label, keyword: label.toLowerCase().replaceAll(' ', '-') }
}

describe('typing anywhere', () => {
  function loseFocus(input: HTMLElement) {
    input.blur()
    expect(document.activeElement).not.toBe(input)
  }

  function type(key: string, target: EventTarget = document.body) {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
  }

  /**
   * jsdom performs no default text insertion, so what is asserted is the part this code is
   * responsible for - focus lands on the input before the character is dispatched. The insertion
   * that follows is the browser's, which is why the handler does not preventDefault and re-type
   * the character itself.
   */
  it('pulls a keystroke aimed at nothing into the command input', () => {
    const input = play()
    loseFocus(input)

    type('n')

    expect(document.activeElement).toBe(input)
  })

  it('leaves the keyboard alone while the builder is up', () => {
    // The game is hidden rather than unmounted, so this listener outlives its own visibility.
    // Every keystroke typed into a builder form would otherwise land here instead.
    const input = play({ active: false })
    loseFocus(input)

    type('n')

    expect(document.activeElement).not.toBe(input)
  })

  it('stops listening once the session is gone', () => {
    const input = play()
    loseFocus(input)
    cleanup()

    type('n')

    expect(document.activeElement).not.toBe(input)
  })

  it('puts the caret back when the tab is returned to', () => {
    const input = play()
    loseFocus(input)

    window.dispatchEvent(new Event('focus'))

    expect(document.activeElement).toBe(input)
  })

  it('does not steal a keystroke already typed into it', () => {
    // Guarding on activeElement keeps the handler from refocusing on every character, which would
    // collapse any selection the player made inside their own half-typed command.
    const input = play()
    input.focus()
    fireEvent.change(input, { target: { value: 'say hello' } })
    input.setSelectionRange(4, 9)

    type('x', input)

    expect(input.selectionStart).toBe(4)
    expect(input.selectionEnd).toBe(9)
  })
})

describe('command history', () => {
  it('is there after a reload', () => {
    const input = play()
    fireEvent.change(input, { target: { value: 'kill rat' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    // A reload is a fresh mount reading the same storage.
    cleanup()
    const reloaded = play()
    fireEvent.keyDown(reloaded, { key: 'ArrowUp' })

    expect(reloaded.value).toBe('kill rat')
  })

  it('does not carry across to another character', () => {
    const input = play({ characterId: 'c1' })
    fireEvent.change(input, { target: { value: 'kill rat' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    cleanup()
    const other = play({ characterId: 'c2' })
    fireEvent.keyDown(other, { key: 'ArrowUp' })

    expect(other.value).toBe('')
  })
})

describe('tab completion', () => {
  it('grows a name from what is in the room', () => {
    const input = play()
    inTheRoom([entry('a bar maiden')])

    fireEvent.change(input, { target: { value: 'talk maid' } })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('talk a bar maiden')
  })

  it('cycles on a second press, and back on shift', () => {
    const input = play()
    inTheRoom([entry('a rat')], [entry('a rat trap')])

    fireEvent.change(input, { target: { value: 'get a rat t' } })
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(input.value).toBe('get a rat trap')

    // Only one candidate has "a rat t" as a prefix, so the cycle is one long and stays put.
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(input.value).toBe('get a rat trap')

    fireEvent.keyDown(input, { key: 'Tab', shiftKey: true })
    expect(input.value).toBe('get a rat trap')
  })

  it('restarts the cycle once the player types again', () => {
    // Without this the second Tab would advance a stale cycle and overwrite the new fragment.
    const input = play()
    inTheRoom([entry('a bar maiden'), entry('an old man')])

    fireEvent.change(input, { target: { value: 'talk maid' } })
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(input.value).toBe('talk a bar maiden')

    fireEvent.change(input, { target: { value: 'talk old' } })
    fireEvent.keyDown(input, { key: 'a' })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('talk an old man')
  })

  it('never offers the player themself', () => {
    // The viewer arrives labelled "you", which is not a name anything answers to.
    const input = play()
    inTheRoom([{ icon: '@', label: 'you', keyword: 'kael' }])

    fireEvent.change(input, { target: { value: 'give beer y' } })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('give beer y')
  })

  it('targets a link-dead player by name rather than by their status', () => {
    const input = play()
    inTheRoom([{ icon: '@', label: 'Mira (link-dead)', keyword: 'mira' }])

    fireEvent.change(input, { target: { value: 'give beer Mi' } })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('give beer Mira')
  })

  it('lets Tab move focus when there is nothing to complete', () => {
    // Swallowing Tab unconditionally would leave the keyboard no way off this control, and
    // combined with the type-anywhere handler that makes the page unnavigable without a mouse.
    const input = play()
    inTheRoom([entry('a bar maiden')])

    fireEvent.change(input, { target: { value: 'kill zzz' } })
    const notPrevented = fireEvent.keyDown(input, { key: 'Tab' })

    expect(notPrevented).toBe(true)
    expect(input.value).toBe('kill zzz')
  })
})

describe('following the newest line', () => {
  /** jsdom reports every box as zero-sized, so the scroll position has to be stated outright. */
  function scrollTo(box: Element, { top, height, visible = 200 }: Record<string, number>) {
    Object.defineProperty(box, 'scrollHeight', { value: height, configurable: true })
    Object.defineProperty(box, 'clientHeight', { value: visible, configurable: true })
    Object.defineProperty(box, 'scrollTop', { value: top, writable: true, configurable: true })
    fireEvent.scroll(box)
  }

  function scrollback() {
    const box = document.querySelector('.scrollback')
    expect(box).not.toBeNull()
    return box as HTMLElement
  }

  it('offers no way back while already at the bottom', () => {
    play()

    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })

  it('offers one once the player scrolls up to read', () => {
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })

    expect(screen.getByRole('button', { name: /jump to newest/i })).toBeTruthy()
  })

  it('keeps following through a couple of lines of movement', () => {
    // The drift. A reading taken while the view has not caught up with the newest line is the
    // page moving under the player, not the player leaving, and it used to stop the transcript.
    play()
    scrollTo(scrollback(), { top: 1750, height: 2000 })

    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })

  it('lets go once the player has scrolled back more than five lines', () => {
    play()
    scrollTo(scrollback(), { top: 1600, height: 2000 })

    expect(screen.getByRole('button', { name: /jump to newest/i })).toBeTruthy()
  })

  it('stops dragging the player back down while they are reading', () => {
    // The whole complaint: a fight still going pulled you to the bottom four times a second.
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })

    emit({ type: 'text', data: { spans: [{ t: 'A rat bites you.' }] } })

    expect(scrollback().scrollTop).toBe(0)
    expect(screen.getByText('A rat bites you.')).toBeTruthy()
  })

  it('goes back to following when the button is used', () => {
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })

    fireEvent.click(screen.getByRole('button', { name: /jump to newest/i }))

    expect(scrollback().scrollTop).toBe(2000)
    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })

  it('resumes following on its own when the player scrolls back down', () => {
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })
    scrollTo(scrollback(), { top: 1800, height: 2000 })

    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })
})

describe('another device taking the character', () => {
  it('hands the screen back rather than offering to fight for it', () => {
    // Two screens that can each reclaim the character is the tug-of-war in a politer costume:
    // the loser reconnects, wins, and the other becomes the loser. The older screen is finished.
    const displaced: string[] = []
    play({ onDisplaced: (message) => displaced.push(message) })

    act(() => stream.displace?.('This character was opened somewhere else.'))

    expect(displaced).toEqual(['This character was opened somewhere else.'])
  })

  it('does not leave the world on the way out', () => {
    // Leaving removes the character from the world, which would pull it out from under the
    // device that has just taken it - this screen's tidiness costing somebody else their session.
    const left: number[] = []
    play({ onLeave: () => left.push(1), onDisplaced: () => {} })

    act(() => stream.displace?.('Opened elsewhere.'))

    expect(left).toHaveLength(0)
  })

  it('offers no reconnect bar, because nothing is going to reconnect', () => {
    play({ onDisplaced: () => {} })

    act(() => stream.displace?.('Opened elsewhere.'))

    expect(screen.queryByRole('button', { name: /rejoin|play here/i })).toBeNull()
  })
})

describe('holding on to the stream', () => {
  it('does not reopen when the parent hands it a new callback', () => {
    // The bug a player hit on the device that had just *won* the character. A callback passed
    // inline is a new function on every parent render, and the parent re-renders on every room
    // change - so the stream was torn down and reopened continuously. Each teardown reports a
    // dropped connection, which marks the character link-dead and completes its event channel, so
    // the replacement stream read a closed one and the screen sat on "Trying to reconnect…".
    const { rerender } = render(
      <GameScreen
        characterId="c1"
        characterName="Kael"
        onLeave={() => {}}
        onDisplaced={() => {}}
      />,
    )

    expect(stream.opened).toBe(1)

    // Three renders, three fresh callback identities, exactly as the parent produces them.
    for (let i = 0; i < 3; i++) {
      rerender(
        <GameScreen
          characterId="c1"
          characterName="Kael"
          onLeave={() => {}}
          onDisplaced={() => {}}
        />,
      )
    }

    expect(stream.opened).toBe(1)
  })

  it('still reaches the newest callback after a rerender', () => {
    // The cost of holding it in a ref would be calling a stale one, which would send the player
    // nowhere when their character was taken.
    const displaced: string[] = []

    const { rerender } = render(
      <GameScreen characterId="c1" characterName="Kael" onLeave={() => {}} onDisplaced={() => {}} />,
    )

    rerender(
      <GameScreen
        characterId="c1"
        characterName="Kael"
        onLeave={() => {}}
        onDisplaced={(message) => displaced.push(message)}
      />,
    )

    act(() => stream.displace?.('Opened elsewhere.'))

    expect(displaced).toEqual(['Opened elsewhere.'])
  })

  it('opens a new stream when the character changes', () => {
    const { rerender } = render(
      <GameScreen characterId="c1" characterName="Kael" onLeave={() => {}} />,
    )

    rerender(<GameScreen characterId="c2" characterName="Mira" onLeave={() => {}} />)

    expect(stream.opened).toBe(2)
  })
})

describe('a command span in the transcript', () => {
  // The offer line a quest giver sends: prose either side, and the command itself clickable.
  // Written the way the server writes it, which is the only shape this has to render.
  function offer() {
    emit({
      type: 'text',
      data: {
        spans: [
          { t: "(fetch-ledger — '", s: 'dim' },
          { t: 'talk elder ledger', s: 'dim', c: 'talk elder ledger' },
          { t: "' to take it on.)", s: 'dim' },
        ],
      },
    })
  }

  it('renders the command as something you can press', () => {
    play()
    offer()

    expect(screen.getByRole('button', { name: 'talk elder ledger' })).toBeTruthy()
  })

  it('runs the command when pressed, rather than typing it into the input', () => {
    const input = play()
    offer()

    fireEvent.click(screen.getByRole('button', { name: 'talk elder ledger' }))

    // Sent, not staged. A contents-panel keyword inserts and lets the player finish the sentence
    // because that click is ambiguous; this one is already a resolved verb and object.
    expect(sent).toEqual(['talk elder ledger'])
    expect(input.value).toBe('')
  })

  it('echoes what it sent, so a span showing a command must run that command', () => {
    play()
    offer()

    fireEvent.click(screen.getByRole('button', { name: 'talk elder ledger' }))

    // The transcript shows the player's own words back. A span that *displayed* a command and ran
    // a different one would put something here they never typed.
    expect(screen.getByText('> talk elder ledger')).toBeTruthy()
  })

  // The other shape: a word inside the giver's own sentence. Its label is prose, not a command,
  // so text and command deliberately differ - and the echo is the point, because watching
  // "talk elder things" appear is how a player learns the sentence rather than just the button.
  function marked() {
    emit({
      type: 'text',
      data: {
        spans: [
          { t: 'Somebody is missing those ', s: null },
          { t: 'things', s: null, c: 'talk elder things' },
          { t: '.', s: null },
        ],
      },
    })
  }

  it('renders a marked word in the prose as a link', () => {
    play()
    marked()

    expect(screen.getByRole('button', { name: 'things' })).toBeTruthy()
    expect(screen.getByText(/Somebody is missing those/)).toBeTruthy()
  })

  it('runs the command the marked word stands for, and shows it', () => {
    const input = play()
    marked()

    fireEvent.click(screen.getByRole('button', { name: 'things' }))

    expect(sent).toEqual(['talk elder things'])
    expect(input.value).toBe('')
    expect(screen.getByText('> talk elder things')).toBeTruthy()
  })

  it('leaves the words around it as ordinary prose', () => {
    play()
    offer()

    expect(screen.queryByRole('button', { name: "(fetch-ledger — '" })).toBeNull()
    expect(screen.getByText(/to take it on/)).toBeTruthy()
  })

  it('renders a span with no command as plain text', () => {
    play()
    emit({ type: 'text', data: { spans: [{ t: 'A rat scurries past.', s: null }] } })

    expect(screen.queryByRole('button', { name: 'A rat scurries past.' })).toBeNull()
    expect(screen.getByText('A rat scurries past.')).toBeTruthy()
  })
})

describe('the group bar', () => {
  function member(name: string, over: Partial<PartyMemberEntry> = {}): PartyMemberEntry {
    return {
      name,
      level: 12,
      path: 'Hallow',
      health: 40,
      healthMax: 80,
      focus: 30,
      focusMax: 60,
      stamina: 90,
      staminaMax: 100,
      isLeader: false,
      here: true,
      linkDead: false,
      ...over,
    }
  }

  function group(...members: PartyMemberEntry[]) {
    emit({ type: 'party', data: { members } })
  }

  it('is absent while ungrouped', () => {
    play()
    expect(screen.queryByRole('group', { name: 'Group' })).toBeNull()
  })

  /** The viewer's own numbers are the row above, in full. Repeating them costs a row. */
  it('does not list the viewer', () => {
    play()
    group(member('Kael', { isLeader: true }), member('Mira'))

    const bar = screen.getByRole('group', { name: 'Group' })
    expect(bar.textContent).toContain('Mira')
    expect(bar.textContent).not.toContain('Kael')
  })

  it('shows each of the three vitals with its numbers', () => {
    play()
    group(member('Kael'), member('Mira'))

    expect(screen.getByLabelText('Mira HP 40 of 80')).toBeTruthy()
    expect(screen.getByLabelText('Mira FO 30 of 60')).toBeTruthy()
    expect(screen.getByLabelText('Mira ST 90 of 100')).toBeTruthy()
  })

  it('says why a member who is fine is not helping', () => {
    play()
    group(member('Kael'), member('Mira', { here: false }), member('Doryn', { linkDead: true }))

    const bar = screen.getByRole('group', { name: 'Group' })
    expect(bar.textContent).toContain('elsewhere')
    expect(bar.textContent).toContain('link-dead')
  })

  /** An empty roster is how leaving a party arrives, so it has to clear the bar. */
  it('disappears again when the group ends', () => {
    play()
    group(member('Kael'), member('Mira'))
    expect(screen.getByRole('group', { name: 'Group' })).toBeTruthy()

    group()
    expect(screen.queryByRole('group', { name: 'Group' })).toBeNull()
  })
})

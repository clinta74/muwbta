// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { shouldRedirectToInput } from './typeAnywhere'

/**
 * The predicate is separated from the effect precisely so this list can exist: every entry is a
 * key that means something else somewhere on the page, and the cost of getting one wrong is a
 * shortcut that silently stops working.
 */
function press(init: KeyboardEventInit & { on?: HTMLElement }): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { bubbles: true, ...init })

  if (init.on) {
    // `target` is set by dispatch, not by the constructor.
    document.body.append(init.on)
    init.on.dispatchEvent(event)
    init.on.remove()
  }

  return event
}

describe('what counts as typing', () => {
  it('redirects a plain letter', () => {
    expect(shouldRedirectToInput(press({ key: 'n' }))).toBe(true)
  })

  it('redirects digits and punctuation, which commands use', () => {
    expect(shouldRedirectToInput(press({ key: '2' }))).toBe(true)
    expect(shouldRedirectToInput(press({ key: "'" }))).toBe(true)
  })

  it('redirects backspace, since a correction is a keystroke too', () => {
    expect(shouldRedirectToInput(press({ key: 'Backspace' }))).toBe(true)
  })
})

describe('keys that already mean something', () => {
  it('leaves copy alone, so scrollback stays selectable', () => {
    // The one that would be noticed immediately: select a transcript, press Ctrl+C, and get an
    // empty clipboard plus the letter c in the input.
    expect(shouldRedirectToInput(press({ key: 'c', ctrlKey: true }))).toBe(false)
    expect(shouldRedirectToInput(press({ key: 'c', metaKey: true }))).toBe(false)
    expect(shouldRedirectToInput(press({ key: 'l', altKey: true }))).toBe(false)
  })

  it('leaves Tab alone, so the page stays keyboard-navigable', () => {
    // Redirecting Tab would return focus to the input on every press and trap it there.
    expect(shouldRedirectToInput(press({ key: 'Tab' }))).toBe(false)
  })

  it('leaves Enter and Space alone, so a focused button still activates', () => {
    expect(shouldRedirectToInput(press({ key: 'Enter' }))).toBe(false)
    expect(shouldRedirectToInput(press({ key: ' ' }))).toBe(false)
  })

  it('leaves navigation and function keys alone', () => {
    for (const key of ['Escape', 'ArrowUp', 'PageDown', 'F5', 'Home']) {
      expect(shouldRedirectToInput(press({ key }))).toBe(false)
    }
  })

  it('leaves a composing keystroke alone, so an IME candidate list survives', () => {
    expect(shouldRedirectToInput(press({ key: 'a', isComposing: true }))).toBe(false)
  })
})

describe('keys already going somewhere that wants them', () => {
  it('leaves another text field alone', () => {
    for (const tag of ['input', 'textarea', 'select']) {
      const element = document.createElement(tag)
      expect(shouldRedirectToInput(press({ key: 'n', on: element }))).toBe(false)
    }
  })

  it('leaves a contenteditable alone', () => {
    const editable = document.createElement('div')
    editable.contentEditable = 'true'

    // jsdom does not implement isContentEditable from the attribute alone.
    Object.defineProperty(editable, 'isContentEditable', { value: true })

    expect(shouldRedirectToInput(press({ key: 'n', on: editable }))).toBe(false)
  })

  it('does redirect from a button, which has no use for a letter', () => {
    const button = document.createElement('button')
    expect(shouldRedirectToInput(press({ key: 'n', on: button }))).toBe(true)
  })
})

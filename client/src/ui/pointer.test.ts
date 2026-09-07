// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { isCoarsePointer } from './pointer'

afterEach(() => {
  vi.unstubAllGlobals()
})

function stubMatchMedia(matches: boolean) {
  const query = vi.fn((q: string) => ({
    matches,
    media: q,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }))

  vi.stubGlobal('matchMedia', query)
  return query
}

it('answers false when matchMedia is missing', () => {
  // jsdom does not implement matchMedia, and the tests run there. Throwing would take down every
  // component that asks the question, in an environment where the honest answer is "not a phone".
  vi.stubGlobal('matchMedia', undefined)

  expect(isCoarsePointer()).toBe(false)
})

it('asks for a finger and no mouse, not merely a small screen', () => {
  const query = stubMatchMedia(true)

  expect(isCoarsePointer()).toBe(true)

  // Both halves matter: `pointer: coarse` alone is true of touch-capable laptops, which have a
  // mouse and should keep the keyboard features. Width is not consulted at all — a narrow desktop
  // window is still a desktop.
  const asked = query.mock.calls[0][0]
  expect(asked).toContain('pointer: coarse')
  expect(asked).toContain('hover: none')
  expect(asked).not.toContain('width')
})

it('answers false for a mouse', () => {
  stubMatchMedia(false)

  expect(isCoarsePointer()).toBe(false)
})

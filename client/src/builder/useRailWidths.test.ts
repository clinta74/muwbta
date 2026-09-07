// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { DEFAULT_RAILS, useRailWidths } from './useRailWidths'

beforeEach(() => localStorage.clear())
afterEach(() => localStorage.clear())

it('starts at the widths the builder shipped with', () => {
  const { result } = renderHook(() => useRailWidths())

  expect(result.current.widths).toEqual(DEFAULT_RAILS)
})

it('remembers a width across a remount', () => {
  // A width you have to set again on every reload is not really adjustable, which is the whole
  // point of the finding.
  const first = renderHook(() => useRailWidths())
  act(() => first.result.current.setLeft(400))
  first.unmount()

  const second = renderHook(() => useRailWidths())

  expect(second.result.current.widths.left).toBe(400)
})

it('clamps a rail dragged past either edge', () => {
  // Dragging past the window is easy, and a rail at zero looks like the tree disappearing rather
  // than like a rail at zero.
  const { result } = renderHook(() => useRailWidths())

  act(() => result.current.setLeft(-500))
  expect(result.current.widths.left).toBeGreaterThan(0)

  act(() => result.current.setLeft(99_999))
  expect(result.current.widths.left).toBeLessThan(99_999)
})

it('resets both rails together', () => {
  const { result } = renderHook(() => useRailWidths())

  act(() => {
    result.current.setLeft(400)
    result.current.setRight(400)
  })
  act(() => result.current.reset())

  expect(result.current.widths).toEqual(DEFAULT_RAILS)
})

it('treats whatever is under the storage key as hostile', () => {
  // The same rule the command history follows: a throw while reading a preference would take the
  // whole builder down rather than costing one setting.
  localStorage.setItem('muwbta.builder.rails', 'not json at all')

  const { result } = renderHook(() => useRailWidths())

  expect(result.current.widths).toEqual(DEFAULT_RAILS)
})

it('ignores a stored value of the wrong shape', () => {
  localStorage.setItem('muwbta.builder.rails', JSON.stringify({ left: 'wide', right: null }))

  const { result } = renderHook(() => useRailWidths())

  expect(result.current.widths).toEqual(DEFAULT_RAILS)
})

// @vitest-environment jsdom
import { expect, it } from 'vitest'
import { followSlack, isAtBottom } from './scrollFollow'

// Five lines at the scrollback's 1.5 line-height, against jsdom's 16px root.
const FIVE_LINES = 120

it('counts the exact bottom', () => {
  expect(isAtBottom({ scrollTop: 800, scrollHeight: 1000, clientHeight: 200 })).toBe(true)
})

it('counts a few lines up, which is the page moving rather than the player', () => {
  // The drift. A single arriving line is taller than the 24px this used to allow, so any reading
  // taken before the view caught up counted as having scrolled away and stopped the transcript.
  expect(isAtBottom({ scrollTop: 750, scrollHeight: 1000, clientHeight: 200 })).toBe(true)
})

it('does not count five full lines back, which takes meaning to do', () => {
  expect(isAtBottom({ scrollTop: 800 - FIVE_LINES - 1, scrollHeight: 1000, clientHeight: 200 }))
    .toBe(false)
})

it('counts a transcript shorter than its box, which never scrolls at all', () => {
  expect(isAtBottom({ scrollTop: 0, scrollHeight: 100, clientHeight: 200 })).toBe(true)
})

it('measures the slack from the element rather than hard-coding pixels', () => {
  // Five lines is a statement about reading, so it has to survive a zoom or a change to the CSS.
  const box = document.createElement('div')
  box.style.lineHeight = '30px'
  document.body.append(box)

  expect(followSlack(box)).toBe(150)
})

it('falls back through the root font size when there is no line height to read', () => {
  // `normal` is a real answer and not a number, which is also what jsdom reports for an element
  // no stylesheet has touched.
  expect(followSlack(document.createElement('div'))).toBe(FIVE_LINES)
})

it('has an answer before there is an element to measure', () => {
  expect(followSlack(null)).toBe(FIVE_LINES)
})

import { expect, it } from 'vitest'
import { reflow } from './reflow'

it('collapses a hard-wrapped paragraph into one that wraps to the window', () => {
  // The complaint. Breaks that are only where somebody's editor ran out of line render as a
  // narrow ragged column on a desktop and in the wrong places on a phone.
  expect(reflow('A ring of dark stone,\ntwin to the dead one\nin Gatetown yard.')).toBe(
    'A ring of dark stone, twin to the dead one in Gatetown yard.',
  )
})

it('keeps a blank line as a paragraph break', () => {
  // The distinction the whole function exists to make: a break that ends a paragraph means
  // something, and a break that is only where the line ran out does not.
  expect(reflow('The ring stands here.\n\nThe way through is up.')).toBe(
    'The ring stands here.\n\nThe way through is up.',
  )
})

it('normalises a run of blank lines to a single paragraph break', () => {
  expect(reflow('One.\n\n\n\nTwo.')).toBe('One.\n\nTwo.')
})

it('keeps the newline that separates the description from the room title', () => {
  // The span arrives prefixed with the `\n` that puts it below the title. Trimming it away as
  // leading whitespace would put the description on the title's line.
  expect(reflow('\nA ring of dark stone.')).toBe('\nA ring of dark stone.')
  expect(reflow('\nOne.\n\nTwo.')).toBe('\nOne.\n\nTwo.')
})

it('leaves prose that was never hard-wrapped exactly as it is', () => {
  // Every authored description in the Reaches is already shaped this way, so the common case has
  // to be a no-op rather than merely a survivable transformation.
  const authored =
    '\nA ring of dark stone, twin to the dead one in Gatetown yard.' +
    '\n\nThis one works. It has always worked.' +
    '\n\nThe way through is up.'

  expect(reflow(authored)).toBe(authored)
})

it('drops trailing whitespace rather than rendering an empty paragraph', () => {
  expect(reflow('\nOne.\n\n')).toBe('\nOne.')
  expect(reflow('\n   \n')).toBe('\n')
})

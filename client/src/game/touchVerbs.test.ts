import { expect, it } from 'vitest'
import { exitPad, recentCommands, verbsFor } from './touchVerbs'

it('keeps every direction on the pad, marking the ones this room has', () => {
  // The load-bearing part. Rendering only the available exits would move the keys under the
  // thumb on every arrival, so the key that was north a moment ago is now south — which is how
  // you walk straight back into the room you just left.
  const pad = exitPad(['north', 'east'])

  expect(pad.map((key) => key.direction)).toEqual([
    'north',
    'east',
    'south',
    'west',
    'up',
    'down',
  ])
  expect(pad.filter((key) => key.available).map((key) => key.direction)).toEqual(['north', 'east'])
})

it('matches exits whatever case they arrive in', () => {
  expect(exitPad(['North', 'DOWN']).filter((k) => k.available).map((k) => k.direction))
    .toEqual(['north', 'down'])
})

it('marks nothing available in a room with no exits', () => {
  expect(exitPad([]).some((key) => key.available)).toBe(false)
})

it('offers the most recent distinct commands, newest first', () => {
  const history = ['look', 'say hello', 'look', 'attack wolf']

  expect(recentCommands(history)).toEqual(['attack wolf', 'look', 'say hello'])
})

it('leaves movement to the exit pad', () => {
  // The pad sits directly below the chips and does this better. Spending chips on directions
  // would crowd out the commands that are genuinely awkward to retype on a phone.
  const history = ['north', 'attack wolf', 'n', 'east', 'up']

  expect(recentCommands(history)).toEqual(['attack wolf'])
})

it('caps the chips and drops blanks', () => {
  const history = ['one', '  ', 'two', 'three', 'four', 'five']

  expect(recentCommands(history)).toEqual(['five', 'four', 'three', 'two'])
  expect(recentCommands(history, 2)).toEqual(['five', 'four'])
})

it('builds verbs around the keyword the server gave', () => {
  expect(verbsFor('wolf')).toContainEqual({ label: 'Attack', command: 'attack wolf' })
})

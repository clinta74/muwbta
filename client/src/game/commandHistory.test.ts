// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { loadHistory, remember, saveHistory } from './commandHistory'

beforeEach(() => localStorage.clear())

describe('surviving a reload', () => {
  it('reads back what was written', () => {
    saveHistory('c1', ['look', 'north'])

    expect(loadHistory('c1')).toEqual(['look', 'north'])
  })

  it('keeps each character to its own', () => {
    // Two characters have different things in reach, so arrowing back through the other one's
    // commands would mostly produce things that no longer work.
    saveHistory('c1', ['kill rat'])
    saveHistory('c2', ['buy beer'])

    expect(loadHistory('c1')).toEqual(['kill rat'])
    expect(loadHistory('c2')).toEqual(['buy beer'])
  })

  it('starts empty for a character that has never played', () => {
    expect(loadHistory('unknown')).toEqual([])
  })
})

describe('what is under the key is not trusted', () => {
  it('survives text that is not JSON', () => {
    // The failure mode this rules out is a throw during the initial useState, which takes the
    // whole game screen down rather than just the history.
    localStorage.setItem('muwbta.history.c1', 'not json at all')

    expect(loadHistory('c1')).toEqual([])
  })

  it('survives JSON of the wrong shape', () => {
    localStorage.setItem('muwbta.history.c1', '{"look":1}')

    expect(loadHistory('c1')).toEqual([])
  })

  it('drops entries that are not commands', () => {
    // A non-string would render as a blank line in the input on the way past.
    localStorage.setItem('muwbta.history.c1', '["look", null, 7, "north"]')

    expect(loadHistory('c1')).toEqual(['look', 'north'])
  })
})

describe('remembering a command', () => {
  it('appends it', () => {
    expect(remember(['look'], 'north')).toEqual(['look', 'north'])
  })

  it('does not record an immediate repeat twice', () => {
    // The value of arrowing up is reaching the command before the one just typed. Ten norths in
    // a row would otherwise bury it ten presses deep.
    expect(remember(['look', 'north'], 'north')).toEqual(['look', 'north'])
  })

  it('does record a repeat that is not immediate', () => {
    expect(remember(['north', 'look'], 'north')).toEqual(['north', 'look', 'north'])
  })

  it('caps the history rather than growing without bound', () => {
    const long = Array.from({ length: 100 }, (_, i) => `command ${i}`)
    const next = remember(long, 'the newest')

    expect(next).toHaveLength(100)
    expect(next[99]).toBe('the newest')
    expect(next[0]).toBe('command 1')
  })

  it('caps what is read back too, in case an older build wrote more', () => {
    saveHistory('c1', Array.from({ length: 250 }, (_, i) => `command ${i}`))

    expect(loadHistory('c1')).toHaveLength(100)
  })
})

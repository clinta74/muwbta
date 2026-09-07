// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MapShell } from './MapShell'

const SHEETS = [
  { world: 'azhen', title: 'Azhen', width: 1105, height: 5330 },
  { world: 'ossara', title: 'Ossara', width: 1105, height: 5520 },
  { world: 'the-unlit', title: 'The Unlit', width: 1105, height: 3155 },
]

vi.mock('../net/api', () => ({
  api: { maps: () => Promise.resolve(SHEETS) },
  mapSheetUrl: (world: string) => `/api/maps/${world}`,
}))

afterEach(cleanup)

const sheet = () => document.querySelector('img.map-sheet')

it('opens on the realm the character is standing in', async () => {
  // Not the first sheet, and not the first alphabetically - the list is sorted by world key, so
  // opening on `sheets[0]` would show Azhen to a player standing in Ossara and look like a bug in
  // the map rather than a default in the viewer.
  render(<MapShell currentWorld="ossara" onClose={() => {}} />)

  await screen.findByAltText('Map of Ossara')
  expect(sheet()?.getAttribute('src')).toBe('/api/maps/ossara')
})

it('falls back to the first sheet when it does not know where you are', async () => {
  // Null before the first room event arrives, which is a real window: the map button is on screen
  // from the moment the game is.
  render(<MapShell currentWorld={null} onClose={() => {}} />)

  expect(await screen.findByAltText('Map of Azhen')).toBeTruthy()
})

it('carries the intrinsic size, so the frame does not reflow when the sheet lands', async () => {
  // These are five times taller than they are wide. Learning that late throws away wherever the
  // player had scrolled to.
  render(<MapShell currentWorld="the-unlit" onClose={() => {}} />)

  const image = await screen.findByAltText('Map of The Unlit')
  expect(image.getAttribute('width')).toBe('1105')
  expect(image.getAttribute('height')).toBe('3155')
})

it('marks which realm you are actually in, separately from the one being read', async () => {
  render(<MapShell currentWorld="ossara" onClose={() => {}} />)

  const ossara = await screen.findByRole('button', { name: /Ossara/ })
  expect(ossara.textContent).toContain('here')

  const azhen = screen.getByRole('button', { name: /Azhen/ })
  expect(azhen.textContent).not.toContain('here')
})

it('switches realms without leaving the map', async () => {
  render(<MapShell currentWorld="ossara" onClose={() => {}} />)
  await screen.findByAltText('Map of Ossara')

  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: /The Unlit/ }))
  })

  expect(screen.getByAltText('Map of The Unlit')).toBeTruthy()

  // Still says where you are. Reading another realm's sheet is not travelling to it.
  expect(screen.getByRole('button', { name: /Ossara/ }).textContent).toContain('here')
})

it('toggles between fitting the width and drawing at full size', async () => {
  // The sheets are unreadable fitted to a phone and unshaped at full size on a desktop, so both
  // settings are the right one somewhere and the control has to say which is in effect.
  render(<MapShell currentWorld="ossara" onClose={() => {}} />)
  await screen.findByAltText('Map of Ossara')

  expect(sheet()?.getAttribute('data-fit')).toBe('width')

  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Full size' }))
  })

  expect(sheet()?.getAttribute('data-fit')).toBe('none')
  expect(screen.getByRole('button', { name: 'Fit width' })).toBeTruthy()
})

it('says so rather than showing an empty frame when the maps will not load', async () => {
  vi.resetModules()
  vi.doMock('../net/api', () => ({
    api: { maps: () => Promise.reject(new Error('offline')) },
    mapSheetUrl: (world: string) => `/api/maps/${world}`,
  }))

  const { MapShell: Offline } = await import('./MapShell')
  render(<Offline currentWorld="ossara" onClose={() => {}} />)

  expect(await screen.findByText('The maps could not be loaded.')).toBeTruthy()
})

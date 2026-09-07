// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { CharacterScreen } from '@/game/AuthScreen'

const ODA = '11111111-1111-1111-1111-111111111111'
const KEHT = '22222222-2222-2222-2222-222222222222'

vi.mock('@/net/api', () => ({
  api: {
    characters: () =>
      Promise.resolve([
        { id: ODA, name: 'Oda', path: 'Warden', level: 12 },
        { id: KEHT, name: 'Keht', path: 'Adept', level: 4 },
      ]),

    // What the server says while the leave that just happened is still in flight: App fires the
    // POST and switches screens in the same tick, so this GET can be answered first and the
    // registry still holds the character. Both rows come back in the world.
    sessions: () =>
      Promise.resolve([{ characterId: ODA }, { characterId: KEHT }]),

    createCharacter: () => Promise.resolve({}),
    enter: () => Promise.resolve({}),
    version: () => Promise.resolve({ version: '0.0.0', revision: 'test', shortRevision: 'test' }),
  },
}))

afterEach(cleanup)

const rowFor = (name: string) =>
  [...document.querySelectorAll('.character-list button')].find(
    (b) => b.querySelector('strong')?.textContent === name,
  )

/**
 * Leaving the world and landing on this screen showed the character you had just walked out as
 * still being in it.
 *
 * Nothing was wrong with the session registry - `Leave` closes it before it does anything else.
 * The screen was simply asking a question whose answer it had already raced past. Awaiting the
 * leave first would fix it and cost the player up to five seconds staring at the game, because
 * that endpoint flushes the character and item save queues before it replies. So the client
 * carries the one id it is certain about instead.
 */
it('does not show a character you have just left as in the world', async () => {
  render(<CharacterScreen onEnter={() => {}} onLogout={() => {}} departed={ODA} />)

  await screen.findByText('Oda')
  expect(rowFor('Oda')?.textContent).not.toContain('in world')
})

/** The other half: one departure must not quietly clear the rest of the list. */
it('still shows the account’s other characters as in the world', async () => {
  render(<CharacterScreen onEnter={() => {}} onLogout={() => {}} departed={ODA} />)

  await screen.findByText('Keht')
  expect(rowFor('Keht')?.textContent).toContain('in world')
})

/**
 * A takeover by another device does not set `departed`, because the character genuinely is in the
 * world - on the device that took it. Marking it gone here would be the more misleading answer,
 * and the player is about to decide whether to take it back.
 */
it('shows every session when the screen was not reached by leaving', async () => {
  render(<CharacterScreen onEnter={() => {}} onLogout={() => {}} notice="Taken over elsewhere." />)

  await screen.findByText('Oda')
  expect(rowFor('Oda')?.textContent).toContain('in world')
  expect(rowFor('Keht')?.textContent).toContain('in world')
})

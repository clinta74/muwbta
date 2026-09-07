// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { Ability } from '@/net/builderApi'

const kick = vi.hoisted(
  (): Ability => ({
    key: 'warden.kick',
    path: 'Warden',
    unlockLevel: 1,
    name: 'Kick',
    description: 'A boot to the knee.',
    costType: 'Stamina',
    costValue: 10,
    cooldownPulses: 24,
    cooldownGroup: 1,
    castTimePulses: null,
    targetingType: 'SingleTarget',
    effects: [{ key: 'damage.physical', params: { scalingFactor: '1.1', minDamage: '3' } }],
    problems: [],
  }),
)

/** On the same Path and the same timer as Kick, so the editor has a name to print. */
const stomp = vi.hoisted(
  (): Ability => ({
    key: 'warden.stomp',
    path: 'Warden',
    unlockLevel: 3,
    name: 'Stomp',
    description: 'The other half of a boot.',
    costType: 'Stamina',
    costValue: 12,
    cooldownPulses: 24,
    cooldownGroup: 1,
    castTimePulses: null,
    targetingType: 'SingleTarget',
    effects: [{ key: 'damage.physical', params: { scalingFactor: '1.2', minDamage: '4' } }],
    problems: [],
  }),
)

/** On a timer of its own, which shares with nothing - the case the list marks. */
const bellow = vi.hoisted(
  (): Ability => ({
    key: 'warden.bellow',
    path: 'Warden',
    unlockLevel: 5,
    name: 'Bellow',
    description: 'Loud, and on a timer nothing else is on.',
    costType: 'Stamina',
    costValue: 14,
    cooldownPulses: 32,
    cooldownGroup: 3,
    castTimePulses: null,
    targetingType: 'Aoe',
    effects: [{ key: 'control.taunt', params: { leadFraction: '0.3' } }],
    problems: [
      { severity: 'Warning', message: 'Warden timer 3 has only this ability on it.' },
    ],
  }),
)

const broken = vi.hoisted(
  (): Ability => ({
    key: 'adept.misfire',
    path: 'Adept',
    unlockLevel: 5,
    name: 'Misfire',
    description: 'Authored against an effect that does not exist.',
    costType: 'Focus',
    costValue: 12,
    cooldownPulses: 24,
    cooldownGroup: null,
    castTimePulses: null,
    targetingType: 'SingleTarget',
    effects: [{ key: 'damage.nonexistent', params: {} }],
    problems: [
      { severity: 'Error', message: "No effect executor is registered for 'damage.nonexistent'." },
    ],
  }),
)

const calls = vi.hoisted(() => ({ list: 0, updated: null as unknown, fail: false }))

vi.mock('@/net/builderApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/net/builderApi')>()
  return {
    ...actual,
    builderApi: {
      abilities: () => {
        calls.list++
        return calls.fail
          ? Promise.reject(new Error('Request failed: 404'))
          : Promise.resolve([kick, stomp, bellow, broken])
      },
      ability: (key: string) => Promise.resolve(key === kick.key ? kick : broken),
      updateAbility: (_key: string, body: unknown) => {
        calls.updated = body
        return Promise.resolve(kick)
      },
    },
  }
})

import { ToastProvider } from '@/ui/Toast'
import { AbilitiesTab } from '@/builder/abilities/AbilitiesTab'

beforeEach(() => {
  calls.list = 0
  calls.updated = null
  calls.fail = false
})

afterEach(cleanup)

function renderTab(path = '/builder/abilities') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <Routes>
          <Route path="/builder/abilities/:abilityKey?" element={<AbilitiesTab />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  )
}

/**
 * The property that matters most, and the reason this file exists at all: the screen actually
 * calls the API. §12's recurring lesson is an endpoint written, checked off, and wired to nothing
 * — the quest editor was claimed in the plan for months while `builderApi`'s quest functions had
 * zero callers. Asserting on rendered text alone would pass against a component holding a
 * hardcoded list.
 */
it('asks the server for the abilities', async () => {
  renderTab()

  await waitFor(() => expect(calls.list).toBeGreaterThan(0))
  expect(await screen.findByText('Kick')).toBeTruthy()
})

it('groups by Path so a progression can be read down the column', async () => {
  renderTab()

  expect(await screen.findByText('Warden')).toBeTruthy()
  expect(await screen.findByText('Adept')).toBeTruthy()
})

it('marks an ability that will not work', async () => {
  // The list is the only place a builder finds out about a row that arrived by import or by hand,
  // since nobody saw a save-time refusal for those.
  renderTab()

  await screen.findByText('Misfire')
  expect(document.querySelector('.ability-flag.bad')).toBeTruthy()
})

it('shows the reason on the ability itself', async () => {
  renderTab('/builder/abilities/adept.misfire')

  expect(await screen.findByText(/No effect executor is registered/)).toBeTruthy()
})

it('saves an edited cooldown, typed in seconds and stored in pulses', async () => {
  // A pulse is an engine detail (PLAN.md §2.3) and had leaked into five builder fields while two
  // beside them took seconds. The editor shows 6s for a 24-pulse cooldown and converts back on
  // the way out, so the stored shape is unchanged.
  renderTab('/builder/abilities/warden.kick')

  const cooldown = await screen.findByDisplayValue('6')
  fireEvent.change(cooldown, { target: { value: '12' } })

  const save = screen.getByRole('button', { name: 'Save' })
  await waitFor(() => expect(save.hasAttribute('disabled')).toBe(false))
  fireEvent.click(save)

  await waitFor(() => expect(calls.updated).not.toBeNull())
  expect((calls.updated as { cooldownPulses: number }).cooldownPulses).toBe(48)
})

it('shows in the list which timer an ability is on', async () => {
  // Reported as "there is no way to manage cooldown groups in the builder UI" - and the control had
  // been in the editor for weeks. The list showed nothing, and the list is the only screen with
  // more than one ability on it, so answering "what shares a timer" meant opening all eighteen
  // Warden abilities one at a time. A feature you cannot see is a feature nobody has.
  renderTab()

  await screen.findByText('Kick')

  // In unlock order down the Path, which is how the list is sorted.
  const badges = [...document.querySelectorAll('.ability-timer')]
  expect(badges.map((b) => b.textContent)).toEqual(['⧗1', '⧗1', '⧗3'])

  // Named, because the number on its own says nothing about what carries it.
  expect(badges[0].getAttribute('title')).toContain('Stomp')
  expect(badges[1].getAttribute('title')).toContain('Kick')
})

it('marks a timer in the list that has nothing else on it', async () => {
  // The same silent-does-nothing the editor warns about and the validator flags, visible without
  // opening anything. Misfire is on no timer at all, so it gets no badge either way.
  renderTab()

  await screen.findByText('Bellow')

  const lonely = [...document.querySelectorAll('.ability-timer.lonely')]
  expect(lonely.length).toBe(1)
  expect(lonely[0].getAttribute('title')).toContain('alone')

  // And an ability on no timer gets no badge at all, so the mark means something.
  expect(document.querySelectorAll('.ability-timer').length).toBe(3)
})

it('names the other abilities on a shared timer', async () => {
  // The timer is the one thing about an ability a builder cannot check by looking at the ability:
  // the number alone says nothing about whether anything else carries it. Read from the roster the
  // tab already loaded, which is the database's answer rather than the catalogue's.
  renderTab('/builder/abilities/warden.kick')

  expect(await screen.findByText(/On Warden timer 1: Stomp/)).toBeTruthy()
})

it('warns when a timer has nothing else on it', async () => {
  // A timer of one is set, shown, and refuses nothing - the silent-does-nothing shape the ability
  // validator exists for, and cheaper to catch here than on the next save.
  renderTab('/builder/abilities/warden.kick')

  const timer = await screen.findByLabelText('Shared timer')
  fireEvent.change(timer, { target: { value: '4' } })
  fireEvent.blur(timer)

  expect(await screen.findByText(/Nothing else is on Warden timer 4/)).toBeTruthy()
})

it('saves a shared timer, and saves clearing one', async () => {
  // Clearing has to be expressible or a timer can never be undone, which is why neither the
  // request nor the endpoint coalesces this field against what is stored.
  renderTab('/builder/abilities/warden.kick')

  const timer = await screen.findByLabelText('Shared timer')
  fireEvent.change(timer, { target: { value: '2' } })
  fireEvent.blur(timer)

  const save = screen.getByRole('button', { name: 'Save' })
  await waitFor(() => expect(save.hasAttribute('disabled')).toBe(false))
  fireEvent.click(save)

  await waitFor(() => expect(calls.updated).not.toBeNull())
  expect((calls.updated as { cooldownGroup: number | null }).cooldownGroup).toBe(2)

  calls.updated = null
  fireEvent.change(timer, { target: { value: '' } })
  fireEvent.blur(timer)

  await waitFor(() => expect(save.hasAttribute('disabled')).toBe(false))
  fireEvent.click(save)

  await waitFor(() => expect(calls.updated).not.toBeNull())
  expect((calls.updated as { cooldownGroup: number | null }).cooldownGroup).toBeNull()
})

it('says so when the list cannot be loaded', async () => {
  // Reported from a dev server: the tab was empty and there was no way to tell an empty database
  // from a request that failed. It was `.catch(() => [])`, so a 404 from a server started before
  // this tab existed rendered as "this game has no abilities" - the exact silent-failure shape the
  // ability validator exists to prevent, reintroduced in the screen built to show it.
  calls.fail = true
  renderTab()

  expect(await screen.findByText(/404/)).toBeTruthy()
})

it('does not offer Save until something changed', async () => {
  // A Save that is always live invites a no-op write, and every write here lands a content_audit
  // row - so "who changed this ability" fills up with saves that changed nothing.
  renderTab('/builder/abilities/warden.kick')

  await screen.findByDisplayValue('6')
  expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true)
})

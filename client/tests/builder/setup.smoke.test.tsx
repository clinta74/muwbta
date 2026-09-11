// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { GameConfiguration, ImportReport } from '@/net/builderApi'

/**
 * What the mocked server accepts. Matches the fixture bundles below, so the tests about importing
 * are about importing — a version mismatch now disables the buttons, which would make every one of
 * them fail for a reason that has nothing to do with what it is checking.
 */
const SERVER_FORMAT_VERSION = vi.hoisted(() => 7)

const reaches = vi.hoisted(
  (): GameConfiguration => ({
    key: 'the-reaches',
    name: 'The Reaches',
    description: 'The new world.',
    startingRoomKey: 'ossara.gatetown.the-gate-yard',
    welcomeMessage: 'Welcome back, {name}.',
    canon: '',
    canonTokens: 0,
    worldKeys: [],
    startingKit: [],
    isActive: false,
    startingRoomExists: true,
    updatedAt: '2026-08-15T00:00:00Z',
  }),
)

const aldenmoor = vi.hoisted(
  (): GameConfiguration => ({
    key: 'aldenmoor-starter',
    name: 'Aldenmoor',
    description: 'The retired starter world.',
    startingRoomKey: 'aldenmoor.millbrook.north-gate',
    welcomeMessage: 'Welcome to Aldenmoor, {name}.',
    canon: '',
    canonTokens: 0,
    worldKeys: [],
    startingKit: [],
    isActive: true,
    startingRoomExists: true,
    updatedAt: '2026-08-01T00:00:00Z',
  }),
)

const calls = vi.hoisted(() => ({
  activated: null as string | null,
  deleted: null as string | null,
  saved: null as string | null,
  imports: [] as boolean[],
  /** Set by the test that checks what a refused apply leaves on screen. The dry run still passes. */
  importFails: false,
}))

const report = vi.hoisted(
  (): ImportReport => ({
    formatVersion: 7,
    dryRun: true,
    counts: [{ kind: 'room', created: 16, updated: 0, removed: 0 }],
    warnings: [],
    failures: [],
    ok: true,
  }),
)

vi.mock('@/net/builderApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/net/builderApi')>()
  return {
    ...actual,
    builderApi: {
      configurations: () =>
        Promise.resolve({
          configurations: [aldenmoor, reaches],
          activeStartingRoomKey: 'aldenmoor.millbrook.north-gate',
          activeWelcomeMessage: 'Welcome to Aldenmoor, {name}.',
        }),
      activateConfiguration: (key: string) => {
        calls.activated = key
        return Promise.resolve({ ...reaches, isActive: true })
      },
      deleteConfiguration: (key: string) => {
        calls.deleted = key
        return Promise.resolve()
      },
      saveConfiguration: (key: string) => {
        calls.saved = key
        return Promise.resolve(reaches)
      },
      worlds: () => Promise.resolve([]),
      exportUrl: (scope?: { world?: string; zone?: string; only?: string; configuration?: string }) =>
        scope?.only ? `/api/builder/export?only=${scope.only}` : '/api/builder/export',
      importBundle: (_bundle: unknown, dryRun: boolean) => {
        calls.imports.push(dryRun)
        // Only the apply fails, which is the case worth covering: the rehearsal came back clean,
        // so the builder is looking at a report that says it will work.
        return calls.importFails && !dryRun
          ? Promise.reject(new Error('The world is locked for maintenance.'))
          : Promise.resolve({ ...report, dryRun })
      },
      // What the running server accepts. The panel compares a loaded file against this and refuses
      // a mismatch before uploading, so it has to be here or every import test renders a panel
      // that cannot say what it reads.
      bundleFormat: () => Promise.resolve({ formatVersion: SERVER_FORMAT_VERSION }),
    },
  }
})

import { ToastProvider } from '@/ui/Toast'
import { SetupTab } from '@/builder/setup/SetupTab'

beforeEach(() => {
  calls.activated = null
  calls.deleted = null
  calls.saved = null
  calls.imports = []
  calls.importFails = false
})

afterEach(cleanup)

function renderSetup(path = '/builder/setup') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <Routes>
          <Route path="/builder/setup/:section?" element={<SetupTab />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  )
}

it('lists the configurations and marks the live one', async () => {
  renderSetup()

  expect(await screen.findByText('Aldenmoor')).toBeTruthy()
  expect(screen.getByText('The Reaches')).toBeTruthy()
  expect(screen.getByText(/· live/)).toBeTruthy()
})

it('offers no way to delete the live configuration', async () => {
  // The server refuses it as well - deleting what the loop is obeying leaves it pointing at
  // values with no row behind them. The button is absent rather than present-and-failing.
  renderSetup()
  await screen.findByText('Aldenmoor')

  // One Delete, for the inactive one, not two.
  expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(1)
})

it('confirms before making a configuration live, and only then activates it', async () => {
  // Choosing which configuration the server obeys is the one action here that changes where every
  // future character wakes up, so it does not happen on a single click.
  renderSetup()
  await screen.findByText('The Reaches')

  fireEvent.click(screen.getByRole('button', { name: 'Make live' }))
  expect(calls.activated).toBeNull()

  // Scoped to the dialog: the row button carries the same words, so an unscoped query would
  // match the one that only opens the prompt and the test would prove nothing.
  const dialog = await screen.findByRole('alertdialog')
  fireEvent.click(within(dialog).getByRole('button', { name: 'Make live' }))

  await waitFor(() => expect(calls.activated).toBe('the-reaches'))
})

it('will not apply an import that has not been dry run', async () => {
  // An import is not atomic, so a failure part way through leaves everything before it applied.
  // The rehearsal is the only way in.
  renderSetup('/builder/setup/transfer')

  const apply = await screen.findByRole('button', { name: 'Apply' }).catch(() => null)
  expect(apply).toBeNull()
  expect(calls.imports).toHaveLength(0)
})

it('dry runs a chosen file before offering to apply it', async () => {
  renderSetup('/builder/setup/transfer')

  const input = (await screen.findByLabelText('Bundle file')) as HTMLInputElement
  const file = new File([JSON.stringify({ formatVersion: 7, rooms: [{}] })], 'gatetown.json', {
    type: 'application/json',
  })

  fireEvent.change(input, { target: { files: [file] } })

  fireEvent.click(await screen.findByRole('button', { name: 'Dry run' }))

  await waitFor(() => expect(calls.imports).toEqual([true]))

  // And the report is shown before Apply becomes usable. Queried as a heading because the
  // button beside it carries the same words.
  expect(await screen.findByRole('heading', { name: 'Dry run' })).toBeTruthy()
  expect(screen.getByText(/16 new/)).toBeTruthy()
})

/** Loads a file and dry runs it, leaving the panel with Apply available. */
async function dryRun() {
  const input = (await screen.findByLabelText('Bundle file')) as HTMLInputElement
  const file = new File([JSON.stringify({ formatVersion: 7, rooms: [{}] })], 'gatetown.json', {
    type: 'application/json',
  })

  fireEvent.change(input, { target: { files: [file] } })
  fireEvent.click(await screen.findByRole('button', { name: 'Dry run' }))
  await waitFor(() => expect(calls.imports).toEqual([true]))
}

/** Clicks Apply and confirms in the dialog. */
async function apply() {
  fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
  const dialog = await screen.findByRole('alertdialog')
  fireEvent.click(within(dialog).getByRole('button', { name: 'Apply' }))
}

it('closes the confirmation once an import has been applied', async () => {
  renderSetup('/builder/setup/transfer')
  await dryRun()
  await apply()

  await waitFor(() => expect(calls.imports).toEqual([true, false]))
  await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
})

it('says the import is done rather than looking ready to run again', async () => {
  // Reported from the builder: after applying, the panel reads as though nothing had happened -
  // the file is still loaded, the buttons are still there, and the only difference is one word in
  // a heading further down the page.
  renderSetup('/builder/setup/transfer')
  await dryRun()
  await apply()

  await waitFor(() => expect(calls.imports).toEqual([true, false]))
  expect(await screen.findByRole('heading', { name: 'Applied' })).toBeTruthy()
  expect(screen.getByText(/applied to this server/i)).toBeTruthy()
})

it('will not apply the same bundle twice without another dry run', async () => {
  // An import is not atomic, and the gate is meant to be "only after a rehearsal". But an *apply*
  // also produces a report, so the flag that gates Apply was satisfied by the apply itself - and
  // the button came straight back, ready to write the whole bundle again.
  renderSetup('/builder/setup/transfer')
  await dryRun()
  await apply()

  await waitFor(() => expect(calls.imports).toEqual([true, false]))

  const again = screen.queryByRole('button', { name: 'Apply' })
  expect(again === null || again.hasAttribute('disabled')).toBe(true)
})

it('keeps a refused import on screen with the reason, rather than resetting', async () => {
  // The confirmation deliberately does not close itself, so a throw leaves it open with the button
  // re-enabled and nothing in it to say why - which reads as the apply having quietly done nothing.
  calls.importFails = true

  renderSetup('/builder/setup/transfer')
  await dryRun()
  await apply()

  await waitFor(() => expect(calls.imports).toEqual([true, false]))

  // The dialog is the one that has to carry it: it stays open by design, so a re-enabled Apply
  // with nothing beside it is the whole of what a builder would see.
  const dialog = await screen.findByRole('alertdialog')
  expect(within(dialog).getByText(/locked for maintenance/)).toBeTruthy()

  // And Apply is offered again rather than the panel pretending the write happened.
  expect(screen.getAllByRole('button', { name: 'Apply' }).length).toBeGreaterThan(0)
})

it('says which bundle format the running server reads', async () => {
  // Answerable while looking at a deployment rather than only while holding a file, which is the
  // point: it is how you tell whether a server has been updated yet.
  renderSetup('/builder/setup/transfer')

  expect(
    await screen.findByText(`This server reads bundle format ${SERVER_FORMAT_VERSION}.`),
  ).toBeTruthy()
})

it('refuses a file the server is too old for, without uploading it', async () => {
  // The scenario this exists for: a bundle authored against a newer build. Nothing the person
  // holding the file can fix by editing it, so the message names the deployment instead.
  renderSetup('/builder/setup/transfer')

  const input = (await screen.findByLabelText('Bundle file')) as HTMLInputElement
  const newer = JSON.stringify({ formatVersion: SERVER_FORMAT_VERSION + 1, rooms: [{}] })

  fireEvent.change(input, {
    target: { files: [new File([newer], 'the-reaches.json', { type: 'application/json' })] },
  })

  expect(await screen.findByText(/The server has not been updated yet/)).toBeTruthy()

  // Disabled rather than allowed-and-refused. The server would reject it anyway; the value is
  // finding out before the upload rather than from a 400.
  expect((await screen.findByRole('button', { name: 'Dry run' })).hasAttribute('disabled')).toBe(true)
  expect(calls.imports).toHaveLength(0)
})

it('offers the abilities on their own, as well as the world', async () => {
  // Asked for while a retune sat in a database and nowhere else. Abilities are content - they live
  // in content/abilities.json and a fresh install seeds from the file - so a change made in the
  // editor has to be able to get back to it. Every other export carries the whole world, and
  // hand-deleting nine collections out of the JSON is a step nobody does twice.
  renderSetup('/builder/setup/transfer')

  const abilities = await screen.findByText('Download abilities only')
  expect(abilities.getAttribute('href')).toBe('/api/builder/export?only=abilities')

  // And the world export is still there, unchanged.
  expect(screen.getByText('Download bundle').getAttribute('href')).toBe('/api/builder/export')
})

it('tells a builder to re-export a file older than the server', async () => {
  // The other direction is a different problem with a different fix, and used to produce the same
  // 400 as the case above.
  renderSetup('/builder/setup/transfer')

  const input = (await screen.findByLabelText('Bundle file')) as HTMLInputElement
  const older = JSON.stringify({ formatVersion: SERVER_FORMAT_VERSION - 1, rooms: [{}] })

  fireEvent.change(input, {
    target: { files: [new File([older], 'stale.json', { type: 'application/json' })] },
  })

  expect(await screen.findByText(/Re-export or re-merge it against this build/)).toBeTruthy()
  expect(calls.imports).toHaveLength(0)
})

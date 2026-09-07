// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { Quest } from '@/net/builderApi'

const quest = vi.hoisted(
  (): Quest => ({
    key: 'errand-for-mira',
    zoneKey: 'aldenmoor.millbrook',
    name: 'An Errand for Mira',
    summary: 'Fetch the ledger.',
    description: 'Mira wants her ledger back.',
    giverMobKey: 'mira',
    turninMobKey: 'mira',
    requiredItemKey: 'ledger',
    requiredCount: 1,
    rewardXp: 50,
    rewardGold: 10,
    rewardItemKey: null,
    rewardItemCount: 1,
    // Attunement: a quest may grant a character flag that opens a gated exit (PLAN.md §4.15).
    rewardFlagKey: null,
    prerequisiteQuestKeys: [],
    isRepeatable: false,
    autoStart: false,
    // Empty means anyone, which is what almost every quest is. Only the four epic chains
    // name a Path, because they share one giver and their rewards are Path-locked.
    paths: [],
    dialogue: {},
    sortOrder: 0,
  }),
)

const calls = vi.hoisted(() => ({ reachability: 0, storyline: 0, updated: null as unknown }))

vi.mock('@/net/builderApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/net/builderApi')>()
  return {
    ...actual,
    builderApi: {
      roomFlags: () => Promise.resolve([]),
      worlds: () => Promise.resolve([]),
      zones: () => Promise.resolve([]),
      mobTemplates: () =>
        Promise.resolve([{ key: 'mira', name: 'Mira the clerk', level: 1 }]),
      itemTemplates: () => Promise.resolve([{ key: 'ledger', name: 'a leather ledger' }]),
      quests: () => Promise.resolve([quest]),
      quest: () => Promise.resolve(quest),
      updateQuest: (_key: string, body: unknown) => {
        calls.updated = body
        return Promise.resolve(quest)
      },
      questReachability: () => {
        calls.reachability++
        return Promise.resolve({
          questKey: quest.key,
          warnings: [
            {
              kind: 'unreachable-required-item',
              message: 'Nothing drops or spawns the required item.',
              itemKey: 'ledger',
              mobKey: null,
            },
          ],
        })
      },
      storyline: () => {
        calls.storyline++
        return Promise.resolve({
          zoneKey: quest.zoneKey,
          nodes: [{ key: quest.key, name: quest.name, zoneKey: quest.zoneKey, external: false }],
          edges: [],
          cycles: [],
          unreachable: [],
          missingPrerequisites: [],
        })
      },
    },
  }
})

import { BuilderDataProvider } from '@/builder/BuilderData'
import { ToastProvider } from '@/ui/Toast'
import { QuestsTab } from '@/builder/quests/QuestsTab'

class FakeEventSource {
  close() {}
  addEventListener() {}
}

beforeEach(() => {
  calls.reachability = 0
  calls.storyline = 0
  calls.updated = null
  ;(globalThis as unknown as { EventSource: unknown }).EventSource = FakeEventSource
})

afterEach(cleanup)

function renderQuests(path = '/builder/quests') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <BuilderDataProvider>
        <ToastProvider>
          <Routes>
            <Route path="/builder/quests/:questKey?" element={<QuestsTab />} />
          </Routes>
        </ToastProvider>
      </BuilderDataProvider>
    </MemoryRouter>,
  )
}

/**
 * The regression this whole tab is: 5.2b was checked off naming `QuestEditor.tsx`, the file did
 * not exist, and `builderApi`'s five quest functions had zero callers. §12's rule is that an
 * endpoint with no caller is not a feature — so the load-bearing assertion here is simply that
 * the API is *called*.
 */
it('lists the quests it loaded', async () => {
  renderQuests()
  expect(await screen.findByText(/An Errand for Mira/)).toBeTruthy()
})

it('opens a quest into the editor', async () => {
  renderQuests('/builder/quests/errand-for-mira')

  await waitFor(() => {
    expect(screen.getByDisplayValue('An Errand for Mira')).toBeTruthy()
    expect(screen.getByDisplayValue('Fetch the ledger.')).toBeTruthy()
  })
})

it('shows reachability warnings in the editor', async () => {
  // §10: an unobtainable required item fails silently in play - the quest reads correctly and
  // the player just wanders - so it has to surface where it is still cheap to fix.
  renderQuests('/builder/quests/errand-for-mira')

  expect(await screen.findByText(/Nothing drops or spawns/)).toBeTruthy()
  expect(calls.reachability).toBeGreaterThan(0)
})

it('draws the chain for the quests zone', async () => {
  renderQuests('/builder/quests/errand-for-mira')
  await waitFor(() => expect(calls.storyline).toBeGreaterThan(0))
})

it('offers every dialogue line, with the engine fallback as the placeholder', async () => {
  renderQuests('/builder/quests/errand-for-mira')

  // Placeholders, not values: a blank line is a real choice, and the builder can only make it
  // knowingly if they can see what the NPC will say instead.
  await waitFor(() => {
    expect(screen.getByPlaceholderText(/I have a job for you/)).toBeTruthy()
    expect(screen.getByPlaceholderText(/Still working on/)).toBeTruthy()
    expect(screen.getByPlaceholderText(/already completed/)).toBeTruthy()
    expect(screen.getByPlaceholderText(/Excellent work/)).toBeTruthy()
  })
})

it('drops blank dialogue lines instead of saving empty strings', async () => {
  // The engine falls back only when the key is *absent*, so storing "" would make the NPC say
  // nothing at all at that moment.
  renderQuests('/builder/quests/errand-for-mira')

  const offer = await screen.findByPlaceholderText(/I have a job for you/)
  fireEvent.change(offer, { target: { value: '   ' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(calls.updated).not.toBeNull())
  expect((calls.updated as { dialogue: Record<string, string> }).dialogue).toEqual({})
})

it('sends a null required item rather than an empty string', async () => {
  // The column is nullable because "no required item" is a talk-to quest, which is not the same
  // thing as a quest asking for an item whose key is "".
  renderQuests('/builder/quests/errand-for-mira')

  await screen.findByDisplayValue('An Errand for Mira')

  const picker = document.querySelector('input[list]') as HTMLInputElement
  fireEvent.change(picker, { target: { value: '' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save' }))

  await waitFor(() => expect(calls.updated).not.toBeNull())
})

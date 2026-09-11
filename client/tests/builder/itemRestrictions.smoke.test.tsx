// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ItemTemplate } from '@/net/builderApi'

/**
 * The three item restrictions, in the editor.
 *
 * The engine has enforced lore, no-drop and Path since they were added, and the API has accepted
 * all three the whole time — but the editor had no control for any of them, so the only way to
 * author one was a bundle import. A builder looking at an epic weapon saw its name and its damage
 * and no hint that it was Warden-only, lore, and bound.
 */
const oathmaul = vi.hoisted(
  (): ItemTemplate => ({
    key: 'epic-warden-1',
    name: 'an unproven oathmaul',
    description: 'Head like a milestone.',
    icon: '/',
    slots: ['MainHand'],
    isTwoHanded: false,
    weight: 2600,
    baseValue: 0,
    baseStats: { damageMin: 5, damageMax: 10 },
    attackDelayPulses: 10,
    attackVerb: 'crush',
    isQuestItem: true,
    isLore: true,
    isNoDrop: true,
    isLightSource: false,
    foodValue: null,
    drinkValue: null,
    useEffects: [],
    useCooldownPulses: null,
    paths: ['Warden'],
  }),
)

const saved = vi.hoisted(() => ({ body: null as Partial<ItemTemplate> | null }))

vi.mock('@/net/builderApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/net/builderApi')>()
  return {
    ...actual,
    builderApi: {
      itemTemplate: () => Promise.resolve(oathmaul),
      updateItemTemplate: (_key: string, body: Partial<ItemTemplate>) => {
        saved.body = body
        return Promise.resolve({ ...oathmaul, ...body } as ItemTemplate)
      },
    },
  }
})

const { ItemTemplateEditor } = await import('@/builder/items/ItemTemplateEditor')
const { ToastProvider } = await import('@/ui/Toast')

afterEach(() => {
  cleanup()
  saved.body = null
})

function open() {
  render(
    <ToastProvider>
      <ItemTemplateEditor templateKey="epic-warden-1" onChanged={() => {}} onDeleted={() => {}} />
    </ToastProvider>,
  )
  return screen.findByText('Restrictions')
}

it('shows what an imported item is already restricted to', async () => {
  await open()

  expect((screen.getByLabelText(/^Lore/) as HTMLInputElement).checked).toBe(true)
  expect((screen.getByLabelText(/^No drop/) as HTMLInputElement).checked).toBe(true)
  expect((screen.getByLabelText('Warden') as HTMLInputElement).checked).toBe(true)
  expect((screen.getByLabelText('Temper') as HTMLInputElement).checked).toBe(false)
})

it('sends all three on save', async () => {
  // The half that matters most. The PATCH treats a missing field as "leave it alone", so an
  // editor that omitted these would look like it worked while changing nothing.
  await open()

  fireEvent.click(screen.getByLabelText('Hallow'))
  fireEvent.click(screen.getByText(/^Save$/))

  await waitFor(() => expect(saved.body).not.toBeNull())
  expect(saved.body).toMatchObject({
    isLore: true,
    isNoDrop: true,
    paths: ['Warden', 'Hallow'],
  })
})

it('carries the light source, which is the same trap one field over', async () => {
  // Not a restriction, but it reaches the editor through the same PATCH and would fail the same
  // silent way: a lantern saved from this screen with the box ticked and the field omitted is a
  // lantern that looks authored and lights nothing.
  await open()

  fireEvent.click(screen.getByLabelText(/^Light source/))
  fireEvent.click(screen.getByText(/^Save$/))

  await waitFor(() => expect(saved.body).not.toBeNull())
  expect(saved.body).toMatchObject({ isLightSource: true })
})

it('keeps the Path list in the enum order however it was ticked', async () => {
  // Hallow is last in the enum and was ticked first here. Two items restricted to the same two
  // Paths should not differ by the order somebody clicked them.
  await open()

  fireEvent.click(screen.getByLabelText('Warden')) // off
  fireEvent.click(screen.getByLabelText('Hallow')) // on
  fireEvent.click(screen.getByLabelText('Adept')) // on
  fireEvent.click(screen.getByText(/^Save$/))

  await waitFor(() => expect(saved.body).not.toBeNull())
  expect(saved.body?.paths).toEqual(['Adept', 'Hallow'])
})

it('unticking every Path sends an empty list, which means anyone', async () => {
  // Empty is a real value here, not "unset". If it were dropped the server would keep the old
  // list and the item could never be un-restricted from this screen.
  await open()

  fireEvent.click(screen.getByLabelText('Warden'))
  fireEvent.click(screen.getByText(/^Save$/))

  await waitFor(() => expect(saved.body).not.toBeNull())
  expect(saved.body?.paths).toEqual([])
})

it('says when a Path list can never be consulted', async () => {
  // A Path restriction on something with no slot restricts nothing: the check runs on wear and
  // wield, and a ground item is neither.
  await open()

  // Untick its only slot, which is what "make this a ground item" is now: slots are checkboxes
  // rather than a select, and each is wrapped in a real label so it answers to getByLabelText.
  fireEvent.click(screen.getByLabelText('MainHand'))

  expect(
    await screen.findByText(/never worn or wielded/),
  ).toBeTruthy()
})

/**
 * A weapon's speed is a decimal, and it could not be typed.
 *
 * The delay field was fully controlled off the stored pulse count, so each keystroke was parsed to
 * seconds, converted to pulses and rendered back: typing "1." parsed as 1, stored 4 pulses, and
 * re-rendered as "1", **erasing the point as it was typed**. On a quarter-second grid that put three
 * of every four speeds out of reach through the UI — including 1.5, which is what the fastest
 * weapons in the game swing at.
 */
it('accepts a decimal attack delay typed one character at a time', async () => {
  await open()

  const field = screen.getByLabelText('Attack delay (seconds)') as HTMLInputElement
  expect(field.value).toBe('2.5')

  fireEvent.change(field, { target: { value: '' } })
  for (const so_far of ['1', '1.', '1.5']) {
    fireEvent.change(field, { target: { value: so_far } })
    // Read back what the field actually holds - if a character is swallowed, the next keystroke
    // builds on the wrong string and this diverges from what was typed.
    expect(field.value).toBe(so_far)
  }

  fireEvent.blur(field)
  fireEvent.click(screen.getByText(/^Save$/))

  await waitFor(() => expect(saved.body).not.toBeNull())
  expect(saved.body).toMatchObject({ attackDelayPulses: 6 })
})

it('sends no delay at all when the field is cleared', async () => {
  // Blank is a real value: the weapon declares no speed and swings at the default in a main hand,
  // and never strikes from an off hand. It must not settle to zero.
  await open()

  fireEvent.change(screen.getByLabelText('Attack delay (seconds)'), { target: { value: '' } })
  fireEvent.blur(screen.getByLabelText('Attack delay (seconds)'))
  fireEvent.click(screen.getByText(/^Save$/))

  await waitFor(() => expect(saved.body).not.toBeNull())
  expect(saved.body).toMatchObject({ attackDelayPulses: null })
})

// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { CharacterScreen } from '@/game/AuthScreen'

vi.mock('@/net/api', () => ({
  api: {
    characters: () => Promise.resolve([]),
    sessions: () => Promise.resolve([]),
    createCharacter: () => Promise.resolve({}),
    enter: () => Promise.resolve({}),
    // CharacterScreen renders the version badge, which asks the server what build it is. This
    // factory replaces the whole module, so anything the screen reaches for has to be here.
    version: () => Promise.resolve({ version: '0.0.0', revision: 'test', shortRevision: 'test' }),
  },
}))

afterEach(cleanup)

/**
 * The structural invariant `.form > label` depends on.
 *
 * jsdom does not apply stylesheets, so nothing here can assert the layout directly. What it can
 * assert is the shape the CSS keys off: field labels are direct children of the form, and a Path
 * row is not, because it lives inside the `paths` fieldset. That separation is the whole fix — a
 * descendant `.form label` rule outspecified `.path` (one class plus one type beats one class)
 * and replaced its grid with `display: block`, so the name and its blurb rendered as
 * "WardenArmored frontline" with a field-sized margin under every row.
 *
 * Flatten the fieldset away and the field-label rule reclaims these rows and the bug returns, so
 * that is what this pins.
 */
it('keeps Path rows out of the direct-child field-label selector', () => {
  const { container } = render(<CharacterScreen onEnter={() => {}} onLogout={() => {}} />)

  const form = container.querySelector('form.form')
  expect(form).not.toBeNull()

  const pathRows = [...container.querySelectorAll('label.path')]
  expect(pathRows).toHaveLength(4)

  for (const row of pathRows) {
    expect(row.parentElement?.classList.contains('paths')).toBe(true)
    expect(row.parentElement).not.toBe(form)
  }

  // The Name field is the case the rule is actually for, and must stay a direct child.
  const fieldLabels = [...(form?.children ?? [])].filter((c) => c.tagName === 'LABEL')
  expect(fieldLabels.length).toBeGreaterThan(0)
})

/**
 * Each row is three separate elements, which is what lets the grid put a gap between them.
 * Concatenating the name and the blurb into one string would look identical until the grid failed
 * and then read as "WardenArmored frontline" again, with no gap to restore.
 */
it('renders the Path name and its blurb as separate elements', () => {
  const { container } = render(<CharacterScreen onEnter={() => {}} onLogout={() => {}} />)

  const hallow = [...container.querySelectorAll('label.path')].find(
    (row) => row.querySelector('strong')?.textContent === 'Hallow',
  )

  expect(hallow).toBeDefined()
  expect(hallow?.querySelector('input[type="radio"]')).not.toBeNull()
  expect(hallow?.querySelector('strong')?.textContent).toBe('Hallow')
  expect(hallow?.querySelector('span')?.textContent).toMatch(/^Support and control\./)
})

it('offers exactly the four Paths the server accepts', () => {
  render(<CharacterScreen onEnter={() => {}} onLogout={() => {}} />)

  // Names, not descriptions: the create endpoint parses these against the CharacterPath enum, so
  // a typo here is a 400 the player cannot do anything about.
  for (const path of ['Warden', 'Adept', 'Temper', 'Hallow']) {
    expect(screen.getByText(path)).toBeTruthy()
  }
})

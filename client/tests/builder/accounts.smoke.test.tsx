// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import type { AdminAccount } from '@/net/adminApi'

const account = vi.hoisted(
  (): AdminAccount => ({
    id: '018f-fake',
    username: 'kael',
    email: 'kael@example.test',
    role: 'Player',
    isBanned: false,
    banReason: null,
    mutedUntil: null,
    createdAt: '2026-01-01T00:00:00Z',
    lastLoginAt: '2026-06-01T12:00:00Z',
    characters: ['Kaelwyn'],
    loginLockedUntil: null,
    registeredFromAddress: '203.0.113.7',
    lastLoginAddress: '203.0.113.8',
  }),
)

const calls = vi.hoisted(() => ({
  password: null as string | null,
  role: null as string | null,
  ban: null as boolean | null,
  mute: null as number | null,
  deleted: null as string | null,
}))

vi.mock('@/net/adminApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/net/adminApi')>()
  return {
    ...actual,
    adminApi: {
      accounts: () => Promise.resolve([account]),
      account: () => Promise.resolve(account),
      setRole: (_username: string, role: string) => {
        calls.role = role
        return Promise.resolve({ ...account, role })
      },
      setBan: (_username: string, banned: boolean) => {
        calls.ban = banned
        return Promise.resolve({ ...account, isBanned: banned })
      },
      setMute: (_username: string, minutes: number | null) => {
        calls.mute = minutes
        return Promise.resolve(account)
      },
      setPassword: (_username: string, password: string) => {
        calls.password = password
        return Promise.resolve(account)
      },
      deleteCharacter: (name: string) => {
        calls.deleted = name
        return Promise.resolve({ message: 'gone' })
      },
    },
  }
})

import { ToastProvider } from '@/ui/Toast'
import { AccountsTab } from '@/builder/accounts/AccountsTab'

beforeEach(() => {
  calls.password = null
  calls.role = null
  calls.ban = null
  calls.mute = null
  calls.deleted = null
})

afterEach(cleanup)

function renderAccounts(path = '/builder/accounts') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <Routes>
          <Route path="/builder/accounts/:username?" element={<AccountsTab />} />
        </Routes>
      </ToastProvider>
    </MemoryRouter>,
  )
}

it('lists the accounts it loaded', async () => {
  renderAccounts()
  expect(await screen.findByText('kael')).toBeTruthy()
})

it('opens an account into the panel', async () => {
  renderAccounts('/builder/accounts/kael')

  await waitFor(() => {
    expect(screen.getByText('kael@example.test')).toBeTruthy()
    expect(screen.getByText('Kaelwyn')).toBeTruthy()
  })
})

it('will not send a password shorter than the policy allows', async () => {
  // The server refuses it too. Refusing here as well means the admin finds out before they have
  // told somebody their new password.
  renderAccounts('/builder/accounts/kael')

  const field = await screen.findByPlaceholderText(/at least 8 characters/)
  fireEvent.change(field, { target: { value: 'short' } })

  const button = screen.getByRole('button', { name: 'Set password' })
  expect((button as HTMLButtonElement).disabled).toBe(true)
  expect(calls.password).toBeNull()
})

it('confirms before setting a password, and only then sends it', async () => {
  // Handing over an account is the one action here with no undo an admin can reach.
  renderAccounts('/builder/accounts/kael')

  const field = await screen.findByPlaceholderText(/at least 8 characters/)
  fireEvent.change(field, { target: { value: 'newpassword1' } })
  fireEvent.click(screen.getByRole('button', { name: 'Set password' }))

  // The dialog is open; nothing has been sent yet.
  expect(calls.password).toBeNull()

  // Resolves to the dialog's button rather than the panel's, because the alert dialog marks the
  // page behind it aria-hidden and role queries skip hidden elements.
  const confirm = await screen.findByRole('button', { name: 'Set password' })
  fireEvent.click(confirm)

  await waitFor(() => expect(calls.password).toBe('newpassword1'))
})

it('bans and mutes through the panel', async () => {
  renderAccounts('/builder/accounts/kael')

  fireEvent.click(await screen.findByRole('button', { name: 'Ban' }))
  await waitFor(() => expect(calls.ban).toBe(true))

  fireEvent.click(screen.getByRole('button', { name: 'Mute' }))
  await waitFor(() => expect(calls.mute).toBe(60))
})

it('confirms before retiring a character', async () => {
  renderAccounts('/builder/accounts/kael')

  fireEvent.click(await screen.findByRole('button', { name: 'Retire' }))
  expect(calls.deleted).toBeNull()

  fireEvent.click(await screen.findByRole('button', { name: 'Retire' }))
  await waitFor(() => expect(calls.deleted).toBe('Kaelwyn'))
})

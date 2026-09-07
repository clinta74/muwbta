import { useState } from 'react'
import { Button } from '../ui/Button'
import { api } from '../net/api'

/** Matches PasswordPolicy on the server. Checked here only to save a round trip. */
const MIN_LENGTH = 8

/**
 * Changing your own password, from the account screen.
 *
 * Collapsed by default: it is a rare action sitting on a screen whose job is picking a character,
 * and an always-open password form is three empty fields between the player and the game.
 */
export function ChangePasswordPanel() {
  const [open, setOpen] = useState(false)
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  function reset() {
    setCurrent('')
    setNext('')
    setConfirm('')
    setError(null)
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setDone(false)

    // Confirmation is checked here and nowhere else - the server never sees it, because a typo
    // you cannot see is a client-side problem and sending it would only be one more copy of the
    // password on the wire.
    if (next !== confirm) {
      setError('The two new passwords do not match.')
      return
    }

    if (next.length < MIN_LENGTH) {
      setError(`Password must be at least ${MIN_LENGTH} characters.`)
      return
    }

    setBusy(true)

    try {
      await api.changePassword(current, next)
      reset()
      setDone(true)
      setOpen(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not change your password.')
    } finally {
      setBusy(false)
    }
  }

  if (!open) {
    return (
      <section className="panel">
        <h2>Account</h2>
        {done && (
          <p className="good">
            Password changed. Everywhere else you were signed in has been signed out.
          </p>
        )}
        <Button variant="link" onClick={() => setOpen(true)}>
          Change password
        </Button>
      </section>
    )
  }

  return (
    <form className="panel form" onSubmit={submit}>
      <h2>Change password</h2>

      <label>
        Current password
        <input
          type="password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          autoComplete="current-password"
        />
      </label>

      <label>
        New password
        <input
          type="password"
          value={next}
          onChange={(e) => setNext(e.target.value)}
          autoComplete="new-password"
          placeholder={`at least ${MIN_LENGTH} characters`}
        />
      </label>

      <label>
        Repeat new password
        <input
          type="password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          autoComplete="new-password"
        />
      </label>

      <p className="detail dim">
        This signs out every other session on your account and returns those characters to the
        character screen. You stay signed in here.
      </p>

      {error && <p className="bad">{error}</p>}

      <Button type="submit" variant="primary" disabled={busy}>
        {busy ? 'Working…' : 'Change password'}
      </Button>

      <Button
       
        variant="link"
        onClick={() => {
          reset()
          setOpen(false)
        }}
      >
        Cancel
      </Button>
    </form>
  )
}

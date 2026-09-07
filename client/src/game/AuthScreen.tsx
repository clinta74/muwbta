import { useEffect, useState } from 'react'
import { Button } from '../ui/Button'
import { VersionBadge } from './VersionBadge'
import { api, type Account, type Character } from '../net/api'
import { ChangePasswordPanel } from './ChangePasswordPanel'

const PATHS = ['Warden', 'Adept', 'Temper', 'Hallow'] as const

const PATH_BLURBS: Record<string, string> = {
  Warden: 'Armored frontline. High health, steady damage.',
  Adept: 'Focus-caster. Fragile, but hits from range.',
  // Not "stealth and burst": there is no stealth in the game, and past level 20 this Path is
  // bleeding rather than burst. What it has is speed and things that keep hurting afterwards.
  Temper: 'Strikes fast, and what it breaks stays broken.',
  Hallow: 'Support and control. Shapes a fight rather than winning it alone.',
}

export function AuthScreen({ onReady }: { onReady: (account: Account) => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      const account =
        mode === 'login'
          ? await api.login(username, password)
          : await api.register(email, username, password)
      onReady(account)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="shell">
      <h1>The Reaches</h1>
      <p className="tagline">Aldenmoor is open.</p>

      <form className="panel form" onSubmit={submit}>
        <div className="tabs">
          <button
            type="button"
            className={mode === 'login' ? 'active' : ''}
            onClick={() => setMode('login')}
          >
            Log in
          </button>
          <button
            type="button"
            className={mode === 'register' ? 'active' : ''}
            onClick={() => setMode('register')}
          >
            Register
          </button>
        </div>

        <label>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" />
        </label>

        {mode === 'register' && (
          <label>
            Email
            <input value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" />
          </label>
        )}

        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
          />
        </label>

        {error && <p className="bad">{error}</p>}

        <Button type="submit" variant="primary" disabled={busy}>
          {busy ? 'Working…' : mode === 'login' ? 'Log in' : 'Create account'}
        </Button>
      </form>
      <VersionBadge />
    </main>
  )
}

export function CharacterScreen({
  onEnter,
  onLogout,
  notice,
  departed,
}: {
  onEnter: (character: Character) => void
  onLogout: () => void

  /**
   * Why the player is looking at this screen, when it was not their idea.
   *
   * Set when another device took the character over. Landing back on the character list with no
   * explanation reads as the game having thrown you out for nothing — and the player is about to
   * click the same character again, so the reason has to be in front of them before they do.
   */
  notice?: string | null

  /**
   * The character this screen was just left with, if it was left rather than arrived at.
   *
   * It exists to settle a race the player would otherwise see: App fires the leave and switches
   * screens in the same tick, so the session list below can be fetched - and answered - before
   * the leave request reaches the registry. The character then reads as still in the world when
   * the player has just walked it out, which is the one row on this screen they are looking at.
   *
   * Deliberately not set when another device takes the character over. It really is in the world
   * then, on somebody else's screen, and saying otherwise would be the more misleading answer.
   */
  departed?: string
}) {
  const [characters, setCharacters] = useState<Character[] | null>(null)
  const [active, setActive] = useState<Set<string>>(new Set())
  const [name, setName] = useState('')
  const [path, setPath] = useState<string>(PATHS[0])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void api
      .characters()
      .then(setCharacters)
      .catch(() => setCharacters([]))

    // Several characters can be in the world at once, so show which already are - otherwise
    // it is impossible to tell from here.
    //
    // Minus the one we know left, because this request races the leave that took it out - see
    // `departed`. The client is the authority on that single id and the server is on the rest.
    void api
      .sessions()
      .then((sessions) =>
        setActive(new Set(sessions.map((s) => s.characterId).filter((id) => id !== departed))),
      )
      .catch(() => undefined)
  }, [departed])

  async function create(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      const created = await api.createCharacter(name, path)
      setCharacters((current) => [...(current ?? []), created])
      setName('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that character.')
    } finally {
      setBusy(false)
    }
  }

  async function enter(character: Character) {
    setError(null)
    try {
      await api.enter(character.id)
      onEnter(character)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not enter the world.')
    }
  }

  return (
    <main className="shell">
      <h1>The Reaches</h1>
      <p className="tagline">Choose a character.</p>

      {notice && (
        <p className="panel bad" role="status">
          {notice}
        </p>
      )}

      <section className="panel">
        <h2>Your characters</h2>
        {characters === null && <p className="dim">Loading…</p>}
        {characters?.length === 0 && <p className="dim">None yet. Make one below.</p>}

        <ul className="character-list">
          {characters?.map((character) => (
            <li key={character.id}>
              <button type="button" onClick={() => void enter(character)}>
                <strong>{character.name}</strong>
                <span className="dim">
                  {character.path} · level {character.level}
                  {active.has(character.id) && <span className="good"> · in world</span>}
                </span>
              </button>
            </li>
          ))}
        </ul>

        <p className="detail dim">
          You can play several characters at once — open each in its own browser tab. The same
          character can only be played in one place: opening it somewhere else closes it here.
        </p>
      </section>

      <form className="panel form" onSubmit={create}>
        <h2>New character</h2>

        <label>
          Name
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="3-16 letters"
            spellCheck={false}
          />
        </label>

        <fieldset className="paths">
          <legend>Path</legend>
          {PATHS.map((option) => (
            <label key={option} className={path === option ? 'path selected' : 'path'}>
              <input
                type="radio"
                name="path"
                value={option}
                checked={path === option}
                onChange={() => setPath(option)}
              />
              <strong>{option}</strong>
              <span className="dim">{PATH_BLURBS[option]}</span>
            </label>
          ))}
        </fieldset>

        {error && <p className="bad">{error}</p>}

        <Button type="submit" variant="primary" disabled={busy}>
          {busy ? 'Working…' : 'Create'}
        </Button>
      </form>

      <ChangePasswordPanel />

      <Button variant="link" onClick={onLogout}>
        Log out
      </Button>
      <VersionBadge />
    </main>
  )
}

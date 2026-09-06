import { useCallback, useEffect, useState } from 'react'
import {
  SCOPE_BLURBS,
  TOKEN_SCOPES,
  tokenApi,
  type AccessToken,
  type AccessTokenList,
  type TokenScope,
} from '../../net/tokenApi'

/** Offered lives, filtered against whatever ceiling the server reports. */
const LIVES = [7, 30, 90]

function when(iso: string | null): string {
  if (!iso) return 'never'
  return new Date(iso).toLocaleDateString()
}

/**
 * Personal access tokens: what this account holds, and the one screen that can mint one.
 *
 * Under Setup rather than Accounts, which is where docs/PAT-AND-MCP.md §9 first put it. Accounts
 * is Admin-only and administers *other people*; a token is personal and every Builder needs one,
 * so showing that tab to builders would have meant exposing the account-administration chrome to
 * gate a single panel. Setup is already the builder-visible home for the things that are not a
 * world, and this is one more of them.
 */
export function TokensPanel() {
  const [list, setList] = useState<AccessTokenList | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [scope, setScope] = useState<TokenScope>('BuilderWrite')
  const [days, setDays] = useState(30)
  const [busy, setBusy] = useState(false)

  // Held only until the page is left. The server cannot show it again, so nothing here may
  // quietly drop it - which is also why it is not put in localStorage: a secret that outlives the
  // moment it is copied is a secret sitting in a browser profile.
  const [minted, setMinted] = useState<{ secret: string; name: string } | null>(null)

  const reload = useCallback(() => {
    void tokenApi
      .list()
      .then((rows) => {
        setList(rows)
        setError(null)
      })
      .catch((e: unknown) => {
        setError(e instanceof Error ? e.message : 'Could not load tokens.')
      })
  }, [])

  useEffect(reload, [reload])

  const create = useCallback(() => {
    if (!name.trim() || busy) return

    setBusy(true)
    void tokenApi
      .create(name.trim(), scope, days)
      .then((created) => {
        setMinted({ secret: created.secret, name: created.token.name })
        setName('')
        setError(null)
        reload()
      })
      .catch((e: unknown) => {
        setError(e instanceof Error ? e.message : 'Could not create the token.')
      })
      .finally(() => setBusy(false))
  }, [name, scope, days, busy, reload])

  const revoke = useCallback(
    (token: AccessToken) => {
      if (!window.confirm(`Revoke '${token.name}'? Anything using it stops working at once.`)) {
        return
      }

      void tokenApi
        .revoke(token.id)
        .then(() => {
          setError(null)
          reload()
        })
        .catch((e: unknown) => {
          setError(e instanceof Error ? e.message : 'Could not revoke the token.')
        })
    },
    [reload],
  )

  const lives = LIVES.filter((d) => d <= (list?.maxLifetimeDays ?? 90))
  const atCap = list ? list.tokens.filter((t) => !t.isExpired).length >= list.maxTokens : false

  return (
    <section className="panel">
      <h2>Access tokens</h2>

      <p className="dim">
        A token lets something other than a browser reach the builder API — an MCP server, a
        script. It can read and edit content and nothing else: not moderation, not administration,
        not the game. It stops working the moment this account is banned, demoted, or its password
        changes.
      </p>

      {error && <p className="bad">{error}</p>}

      {minted && (
        <div className="callout">
          <h3>Copy “{minted.name}” now</h3>
          <p>This is the only time it is shown. There is no way to see it again.</p>
          <textarea readOnly rows={3} value={minted.secret} onFocus={(e) => e.target.select()} />
          <div className="row">
            <button
              type="button"
              onClick={() => void navigator.clipboard?.writeText(minted.secret)}
            >
              Copy
            </button>
            <button type="button" className="ghost" onClick={() => setMinted(null)}>
              I have it
            </button>
          </div>
        </div>
      )}

      <div className="form-grid">
        <label htmlFor="token-name">Name</label>
        <input
          id="token-name"
          value={name}
          maxLength={64}
          placeholder="the laptop's MCP server"
          onChange={(e) => setName(e.target.value)}
        />

        <label htmlFor="token-scope">Scope</label>
        <div>
          <select
            id="token-scope"
            value={scope}
            onChange={(e) => setScope(e.target.value as TokenScope)}
          >
            {TOKEN_SCOPES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
          <p className="dim">{SCOPE_BLURBS[scope]}</p>
        </div>

        <label htmlFor="token-days">Expires</label>
        <div>
          <select
            id="token-days"
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
          >
            {lives.map((d) => (
              <option key={d} value={d}>
                in {d} days
              </option>
            ))}
          </select>
          {/* There is no "never" and the absence is deliberate, so it is worth saying rather
              than leaving somebody hunting for the option. */}
          <p className="dim">Every token expires. Renew it by making a new one.</p>
        </div>
      </div>

      <div className="row">
        <button type="button" disabled={!name.trim() || busy || atCap} onClick={create}>
          {busy ? 'Creating…' : 'Create token'}
        </button>
        {atCap && (
          <span className="dim">
            {list?.maxTokens} live tokens is the limit. Revoke one to make another.
          </span>
        )}
      </div>

      <h3>This account&rsquo;s tokens</h3>

      {list === null ? (
        <p className="dim">Loading…</p>
      ) : list.tokens.length === 0 ? (
        <p className="dim">None yet.</p>
      ) : (
        <table className="grid">
          <thead>
            <tr>
              <th>Name</th>
              <th>Scope</th>
              <th>Created</th>
              <th>Expires</th>
              <th>Last used</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {list.tokens.map((token) => (
              <tr key={token.id} className={token.isExpired ? 'dim' : undefined}>
                <td>{token.name}</td>
                <td>{token.scope}</td>
                <td>{when(token.createdAt)}</td>
                <td>{token.isExpired ? `expired ${when(token.expiresAt)}` : when(token.expiresAt)}</td>
                <td>{when(token.lastUsedAt)}</td>
                <td>
                  <button type="button" className="ghost" onClick={() => revoke(token)}>
                    Revoke
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

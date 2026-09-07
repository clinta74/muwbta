import { useCallback, useEffect, useState } from 'react'
import {
  SCOPE_BLURBS,
  TOKEN_SCOPES,
  tokenApi,
  type AccessToken,
  type AccessTokenList,
  type TokenScope,
} from '../../net/tokenApi'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { Textarea } from '../../ui/Textarea'

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
 *
 * <b>This panel was written against four classes that did not exist.</b> `.form-grid`, `.grid`,
 * `.callout` and `.ghost` matched no rule in any of the stylesheets, so the form was an unstyled
 * stack, the table was browser-default — which is why the gap between a token's name and its
 * scope looked wrong; there was no gap, only whatever the user agent chose — the one block whose
 * job is "you will not see this secret again" had no frame, and Revoke rendered as an ordinary
 * button. Three of the four now exist as shared primitives; `.ghost` does not, because the quiet
 * button it wanted is the bare `<button>` and the destructive one is `Button variant="danger"`.
 */
export function TokensPanel() {
  const [list, setList] = useState<AccessTokenList | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [scope, setScope] = useState<TokenScope>('BuilderWrite')
  const [days, setDays] = useState(30)
  const [busy, setBusy] = useState(false)

  /** The token a confirmation is open for, or null. Revoking cannot be undone. */
  const [revoking, setRevoking] = useState<AccessToken | null>(null)

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
      setBusy(true)
      void tokenApi
        .revoke(token.id)
        .then(() => {
          setError(null)
          setRevoking(null)
          reload()
        })
        .catch((e: unknown) => {
          setError(e instanceof Error ? e.message : 'Could not revoke the token.')
        })
        .finally(() => setBusy(false))
    },
    [reload],
  )

  const lives = LIVES.filter((d) => d <= (list?.maxLifetimeDays ?? 90))
  const atCap = list ? list.tokens.filter((t) => !t.isExpired).length >= list.maxTokens : false

  return (
    <section className="panel setup-panel">
      <div className="setup-head">
        <h3>Access tokens</h3>
      </div>

      <p className="dim">
        A token lets something other than a browser reach the builder API — an MCP server, a
        script. It can read and edit content and nothing else: not moderation, not administration,
        not the game. It stops working the moment this account is banned, demoted, or its password
        changes.
      </p>

      {error && <p className="bad">{error}</p>}

      {minted && (
        <div className="callout">
          <h4>Copy “{minted.name}” now</h4>
          <p>This is the only time it is shown. There is no way to see it again.</p>
          <Textarea readOnly value={minted.secret} rows={3} onChange={() => {}} />
          <div className="row">
            <Button
              variant="primary"
              onClick={() => void navigator.clipboard?.writeText(minted.secret)}
            >
              Copy
            </Button>
            <Button onClick={() => setMinted(null)}>I have it</Button>
          </div>
        </div>
      )}

      {/* One line at a full rail, wrapping to two when it narrows: a name is most of the width,
          and a scope and a life are both a phrase. They used to be three stacked rows of
          full-width control, which made minting a token look like filling in a form when it is
          really answering one question three ways. */}
      <div className="field-row">
        <Field label="Name" width="lg">
          <input
            value={name}
            maxLength={64}
            placeholder="the laptop's MCP server"
            onChange={(e) => setName(e.target.value)}
          />
        </Field>

        <Field label="Scope" width="md" hint={SCOPE_BLURBS[scope]}>
          <Select value={scope} onChange={(value) => setScope(value as TokenScope)}>
            {TOKEN_SCOPES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </Select>
        </Field>

        {/* There is no "never" and the absence is deliberate, so it is worth saying rather
            than leaving somebody hunting for the option. */}
        <Field label="Expires" width="sm" hint="Every token expires. Renew it by making a new one.">
          <Select value={String(days)} onChange={(value) => setDays(Number(value))}>
            {lives.map((d) => (
              <option key={d} value={d}>
                in {d} days
              </option>
            ))}
          </Select>
        </Field>
      </div>

      <div className="row">
        <Button variant="primary" disabled={!name.trim() || busy || atCap} onClick={create}>
          {busy ? 'Creating…' : 'Create token'}
        </Button>
        {atCap && (
          <span className="dim">
            {list?.maxTokens} live tokens is the limit. Revoke one to make another.
          </span>
        )}
      </div>

      <h4>This account&rsquo;s tokens</h4>

      {list === null ? (
        <p className="dim">Loading…</p>
      ) : list.tokens.length === 0 ? (
        <p className="dim">None yet.</p>
      ) : (
        <div className="table-wrap">
          <table className="data-table">
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
                  <td>
                    {token.isExpired ? `expired ${when(token.expiresAt)}` : when(token.expiresAt)}
                  </td>
                  <td>{when(token.lastUsedAt)}</td>
                  <td>
                    <Button variant="danger" onClick={() => setRevoking(token)}>
                      Revoke
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Was a native `confirm()`, which every other destructive action in the builder stopped
          using when ConfirmDialog was written. */}
      <ConfirmDialog
        open={revoking !== null}
        onOpenChange={(open) => {
          if (!open) setRevoking(null)
        }}
        title={revoking ? `Revoke “${revoking.name}”?` : 'Revoke this token?'}
        description="Anything using it stops working at once. A revoked token cannot be restored — mint a new one and paste it wherever this one was."
        confirmLabel="Revoke"
        destructive
        busy={busy}
        onConfirm={() => {
          if (revoking) revoke(revoking)
        }}
      />
    </section>
  )
}

import { useEffect, useState } from 'react'
import {
  builderApi,
  type GameConfiguration,
  type GameConfigurationList,
  type WorldSummary,
} from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { useToast } from '../../ui/Toast'

const NAME_TOKEN = '{name}'

/** A key the server will accept: lowercase, digits, inner hyphens. */
function isValidKey(key: string): boolean {
  return /^[a-z][a-z0-9-]*[a-z0-9]$|^[a-z]$/.test(key)
}

/** Three dot-separated segments, which is what a RoomKey is. */
function isValidRoomKey(key: string): boolean {
  return /^[a-z0-9][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*){2}$/.test(key)
}

interface Draft {
  key: string
  name: string
  description: string
  startingRoomKey: string
  welcomeMessage: string
  canon: string
  worldKeys: string[]
  /** False for a new one, so the key field is editable exactly once. */
  existing: boolean
}

function draftOf(configuration: GameConfiguration): Draft {
  return { ...configuration, existing: true }
}

const BLANK: Draft = {
  key: '',
  name: '',
  description: '',
  startingRoomKey: '',
  welcomeMessage: 'Welcome back, {name}.',
  canon: '',
  worldKeys: [],
  existing: false,
}

interface Props {
  list: GameConfigurationList | null
  onChanged: () => void
}

/**
 * Starter configurations (PLAN.md §4.16): a server holds several complete answers to "what does a
 * new player meet", and exactly one is live.
 *
 * <b>Writing one and choosing it are separate buttons, on purpose.</b> They are decisions with very
 * different blast radii — a typo in a greeting is a bad line of text, and a typo in the active
 * configuration is every new character waking up in the wrong world — so the panel does not let a
 * save quietly become a switch.
 */
export function ConfigurationsPanel({ list, onChanged }: Props) {
  const toast = useToast()
  const [draft, setDraft] = useState<Draft | null>(null)

  // The worlds on this server, for the tag list. Loaded here rather than passed down because the
  // rest of the Setup tab has no use for them; an empty list is a server with no worlds yet.
  const [worlds, setWorlds] = useState<WorldSummary[]>([])
  useEffect(() => {
    void builderApi
      .worlds()
      .then(setWorlds)
      .catch(() => setWorlds([]))
  }, [])
  const [busy, setBusy] = useState(false)
  const [confirming, setConfirming] = useState<GameConfiguration | null>(null)
  const [activating, setActivating] = useState<GameConfiguration | null>(null)

  // Drop an open editor when the list underneath it is replaced, so a stale draft cannot be
  // saved over somebody else's change without the panel having shown it.
  useEffect(() => setDraft(null), [list])

  if (!list) {
    return <p className="dim">Loading…</p>
  }

  const keyError =
    draft && !draft.existing && draft.key !== '' && !isValidKey(draft.key)
      ? 'Lowercase letters, digits and inner hyphens — e.g. the-reaches.'
      : null

  const roomError =
    draft && draft.startingRoomKey !== '' && !isValidRoomKey(draft.startingRoomKey)
      ? 'Three dot-separated segments — e.g. ossara.gatetown.the-gate-yard.'
      : null

  const canSave =
    draft !== null &&
    draft.key !== '' &&
    draft.name.trim() !== '' &&
    keyError === null &&
    roomError === null &&
    draft.startingRoomKey !== ''

  /**
   * A live figure while somebody types, from the server's own ratio, against the server's own
   * budget - the panel keeps no number of its own. Over budget is a warning, not a refusal: an
   * over-long prompt is truncated by the model rather than rejected, so this is the one place a
   * builder is told.
   */
  const canonEstimate = (() => {
    if (!draft || !list) return null
    const chars = draft.canon.trim().length
    const tokens = chars === 0 ? null : Math.round(chars / list.canonCharsPerToken)
    if (tokens === null) return <>No canon: the assist is told there is no world description.</>
    const over = tokens > list.canonTokenBudget
    return (
      <span className={over ? 'bad' : undefined}>
        About {tokens.toLocaleString()} of {list.canonTokenBudget.toLocaleString()} tokens
        {over ? '. Over budget: the model will not read all of it.' : '.'}
      </span>
    )
  })()


  /** Which other configuration already lists a world, or null when it is free or ours. */
  function ownerOf(worldKey: string): string | null {
    const owner = list?.configurations.find(
      (c) => c.key !== draft?.key && c.worldKeys.includes(worldKey),
    )
    return owner ? owner.name || owner.key : null
  }

  function toggleWorld(worldKey: string) {
    if (!draft) return
    setDraft({
      ...draft,
      worldKeys: draft.worldKeys.includes(worldKey)
        ? draft.worldKeys.filter((k) => k !== worldKey)
        : [...draft.worldKeys, worldKey],
    })
  }

  async function save() {
    if (!draft || !canSave) return

    setBusy(true)
    try {
      await builderApi.saveConfiguration(draft.key, {
        name: draft.name,
        description: draft.description,
        startingRoomKey: draft.startingRoomKey,
        welcomeMessage: draft.welcomeMessage,
        canon: draft.canon,
        worldKeys: draft.worldKeys,
      })
      toast.notify(draft.existing ? 'Configuration saved.' : 'Configuration created.')
      setDraft(null)
      onChanged()
    } catch (e: unknown) {
      toast.notify(e instanceof Error ? e.message : 'Could not save.', 'bad')
    } finally {
      setBusy(false)
    }
  }

  async function activate(configuration: GameConfiguration) {
    setBusy(true)
    try {
      await builderApi.activateConfiguration(configuration.key)
      toast.notify(`${configuration.name} is live. New characters start there from now.`)
      setActivating(null)
      onChanged()
    } catch (e: unknown) {
      toast.notify(e instanceof Error ? e.message : 'Could not activate.', 'bad')
    } finally {
      setBusy(false)
    }
  }

  async function remove(configuration: GameConfiguration) {
    setBusy(true)
    try {
      await builderApi.deleteConfiguration(configuration.key)
      toast.notify('Configuration deleted.')
      setConfirming(null)
      onChanged()
    } catch (e: unknown) {
      toast.notify(e instanceof Error ? e.message : 'Could not delete.', 'bad')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel setup-panel">
      <div className="setup-head">
        <h3>Starter configurations</h3>
        <Button variant="primary" onClick={() => setDraft(BLANK)}>
          New configuration
        </Button>
      </div>

      <p className="dim">
        Where a new character wakes up and what the game says to them. These travel in an exported
        bundle, so a starter set can be moved to another server — but which one is <em>live</em>
        never travels, so importing content can never repoint a running server.
      </p>

      {list.configurations.length === 0 && !draft && (
        <p className="dim">
          None yet. The server is using its built-in fallback — <code>{list.activeStartingRoomKey}</code>.
        </p>
      )}

      <ul className="setup-list">
        {list.configurations.map((configuration) => (
          <li key={configuration.key} className="setup-row">
            <div className="setup-row-main">
              <strong>{configuration.name}</strong>
              <code className="dim"> {configuration.key}</code>
              {configuration.isActive && <span className="good"> · live</span>}

              <p className="dim">{configuration.description}</p>

              <p className="dim">
                Starts at <code>{configuration.startingRoomKey}</code>
                {!configuration.startingRoomExists && (
                  // A warning rather than a refusal: writing the configuration before importing
                  // the world it names is the normal order of operations on a fresh server.
                  <span className="bad"> · no such room here yet</span>
                )}
              </p>

              <p className="dim">{configuration.welcomeMessage}</p>

              {configuration.worldKeys.length > 0 && (
                <p className="dim">
                  Worlds:{' '}
                  {configuration.worldKeys.map((k, i) => (
                    <span key={k}>
                      {i > 0 && ', '}
                      <code>{k}</code>
                    </span>
                  ))}
                </p>
              )}
            </div>

            <div className="spawner-actions">
              <Button onClick={() => setDraft(draftOf(configuration))}>Edit</Button>

              {/* The whole configuration as one file: its worlds, and itself with its canon. A
                  real link, so the browser honours the attachment filename. Absent until the
                  configuration tags a world, because an empty bundle is a surprise to save. */}
              {configuration.worldKeys.length > 0 && (
                <a
                  className="setup-download"
                  href={builderApi.exportUrl({ configuration: configuration.key })}
                >
                  Download bundle
                </a>
              )}

              {!configuration.isActive && (
                <Button variant="primary" onClick={() => setActivating(configuration)}>
                  Make live
                </Button>
              )}

              {/* The live one has no Delete at all rather than a Delete that fails. The server
                  refuses it too; this is so the button never lies about what it will do. */}
              {!configuration.isActive && (
                <Button variant="danger" onClick={() => setConfirming(configuration)}>
                  Delete
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>

      {draft && (
        <div className="section-body">
          <h4>{draft.existing ? `Edit ${draft.name}` : 'New configuration'}</h4>

          {!draft.existing && (
            <Field label="Key" error={keyError} hint="Permanent. Used in exports and in the API.">
              <input
                value={draft.key}
                spellCheck={false}
                placeholder="the-reaches"
                onChange={(e) => setDraft({ ...draft, key: e.target.value })}
              />
            </Field>
          )}

          <Field label="Name">
            <input
              value={draft.name}
              placeholder="The Reaches"
              onChange={(e) => setDraft({ ...draft, name: e.target.value })}
            />
          </Field>

          <Field label="Description" hint="What this configuration is for, in a sentence.">
            <Textarea
              rows={2}
              value={draft.description}
              onChange={(value) => setDraft({ ...draft, description: value })}
            />
          </Field>

          <Field
            label="Starting room"
            error={roomError}
            hint="Where new characters begin, and where anyone whose saved room is gone is put."
          >
            <input
              value={draft.startingRoomKey}
              spellCheck={false}
              placeholder="ossara.gatetown.the-gate-yard"
              onChange={(e) => setDraft({ ...draft, startingRoomKey: e.target.value })}
            />
          </Field>

          <Field
            label="Welcome message"
            hint={
              <>
                Sent on entering the game. <code>{NAME_TOKEN}</code> becomes the character's name;
                leave it out and the line is sent as written.
              </>
            }
          >
            <Textarea
              rows={2}
              value={draft.welcomeMessage}
              onChange={(value) => setDraft({ ...draft, welcomeMessage: value })}
            />
          </Field>

          <Field
            label="Worlds"
            hint="Which worlds this configuration is for. A world belongs to one configuration, and the row's Download bundle exports these together with the configuration and its canon."
          >
            <ul className="room-picker">
              {worlds.length === 0 && <li className="dim">No worlds on this server yet.</li>}
              {worlds.map((world) => {
                const owner = ownerOf(world.key)
                return (
                  <li key={world.key}>
                    <label className="field-check">
                      <input
                        type="checkbox"
                        disabled={owner !== null}
                        checked={draft.worldKeys.includes(world.key)}
                        onChange={() => toggleWorld(world.key)}
                      />
                      {world.name || world.key} <code className="dim">{world.key}</code>
                      {owner !== null && <span className="dim"> · belongs to {owner}</span>}
                    </label>
                  </li>
                )
              })}
            </ul>
          </Field>

          <Field
            label="Canon"
            hint={
              <>
                What the builder assist is told about this world before every request, as
                markdown. For the Reaches it is <code>docs/WORLD.md</code> above the marker, which
                the merge tool writes in; for any other world, write it here. It takes effect for
                the assist while this configuration is live, on the next request.{' '}
                {canonEstimate}
              </>
            }
          >
            <Textarea
              rows={14}
              maxRows={20}
              value={draft.canon}
              onChange={(value) => setDraft({ ...draft, canon: value })}
            />
          </Field>

          <div className="spawner-actions">
            <Button variant="primary" disabled={!canSave || busy} onClick={() => void save()}>
              {busy ? 'Saving…' : 'Save'}
            </Button>
            <Button onClick={() => setDraft(null)}>Cancel</Button>
          </div>

          {draft.existing && (
            <p className="dim">
              Saving changes what this configuration means. It does not make it live.
            </p>
          )}
        </div>
      )}

      <ConfirmDialog
        open={activating !== null}
        onOpenChange={(open) => !open && setActivating(null)}
        title={`Make ${activating?.name ?? ''} live?`}
        description={
          <>
            Every character created from now on starts at{' '}
            <code>{activating?.startingRoomKey}</code>, and so does anyone whose saved room no
            longer exists. Players already in the world are not moved.
            {activating && !activating.startingRoomExists && (
              <>
                {' '}
                <strong>That room does not exist on this server yet.</strong>
              </>
            )}
          </>
        }
        confirmLabel="Make live"
        busy={busy}
        onConfirm={() => activating && void activate(activating)}
      />

      <ConfirmDialog
        open={confirming !== null}
        onOpenChange={(open) => !open && setConfirming(null)}
        title={`Delete ${confirming?.name ?? ''}?`}
        description="The configuration is removed. No world content is touched."
        confirmLabel="Delete"
        destructive
        busy={busy}
        onConfirm={() => confirming && void remove(confirming)}
      />
    </section>
  )
}

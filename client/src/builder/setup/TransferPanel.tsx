import { useEffect, useRef, useState } from 'react'
import { builderApi, type ImportReport } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { useToast } from '../../ui/Toast'

interface Loaded {
  filename: string
  bundle: unknown
  formatVersion: number
  /** A one-line count per kind, so a file can be recognised before it is applied. */
  summary: string
}

/** What a bundle claims to hold, without trusting it to be well-formed. */
function describe(bundle: unknown): string {
  if (typeof bundle !== 'object' || bundle === null) return 'not a bundle'

  const record = bundle as Record<string, unknown>
  const parts: string[] = []

  for (const key of [
    'worlds',
    'zones',
    'rooms',
    'itemTemplates',
    'mobTemplates',
    'abilities',
    'spawners',
    'quests',
    'configurations',
  ]) {
    const value = record[key]
    if (Array.isArray(value) && value.length > 0) {
      parts.push(`${value.length} ${key}`)
    }
  }

  return parts.length > 0 ? parts.join(', ') : 'empty'
}

/**
 * Whether the server will take this file, said in the words the situation actually calls for.
 *
 * The two directions are different problems and used to produce the same 400. A file *older* than
 * the server is a file to re-export; a file *newer* than the server means the server has not been
 * updated yet, which is nothing the person holding the file can fix by editing it.
 */
function verdict(fileVersion: number, serverVersion: number | null): string | null {
  if (serverVersion === null || fileVersion === serverVersion) return null

  if (fileVersion > serverVersion) {
    return (
      `This file is format ${fileVersion} and this server reads ${serverVersion}. ` +
      'The server has not been updated yet — deploy it before importing.'
    )
  }

  return (
    `This file is format ${fileVersion} and this server reads ${serverVersion}. ` +
    'Re-export or re-merge it against this build.'
  )
}

interface Props {
  onImported: () => void
}

/**
 * Moving authored content between servers (PLAN.md §6).
 *
 * <b>A dry run is not optional here, it is the only way in.</b> An import is not atomic — one
 * entity is one loop round trip and one transaction — so a bundle that fails part way through
 * leaves everything before it applied. The rehearsal answers the same question from the same code
 * and touches nothing, so the panel makes it the first step rather than a checkbox somebody
 * remembers.
 *
 * Export is a plain link. The response carries a Content-Disposition attachment with a dated
 * filename, and fetching it into a blob would throw that away for an untitled download.
 */
export function TransferPanel({ onImported }: Props) {
  const toast = useToast()
  const fileInput = useRef<HTMLInputElement>(null)

  const [scope, setScope] = useState({ world: '', zone: '' })
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [report, setReport] = useState<ImportReport | null>(null)
  const [busy, setBusy] = useState(false)
  const [confirming, setConfirming] = useState(false)

  /**
   * Why the last run was refused, or null. Held rather than only toasted: the confirmation does
   * not close itself on a throw, so without this the dialog comes back to its resting state with
   * Apply available again and nothing in it saying the import did not happen.
   */
  const [error, setError] = useState<string | null>(null)

  /**
   * What this server accepts. Null while unknown — either still loading or the request failed — and
   * an unknown version must never block an import: the server refuses a bad one anyway, and a panel
   * that stopped working because a metadata read failed would be worse than one that says nothing.
   */
  const [serverVersion, setServerVersion] = useState<number | null>(null)

  useEffect(() => {
    let live = true

    builderApi
      .bundleFormat()
      .then((info) => {
        if (live) setServerVersion(info.formatVersion)
      })
      .catch(() => {
        // Deliberately silent. Not knowing is the default state and is already handled everywhere
        // it matters; a toast here would fire on every builder open against an older server.
      })

    return () => {
      live = false
    }
  }, [])

  const mismatch = loaded ? verdict(loaded.formatVersion, serverVersion) : null

  function reset() {
    setLoaded(null)
    setReport(null)
    setError(null)
    if (fileInput.current) fileInput.current.value = ''
  }

  /**
   * Whether the bundle on screen has already been written.
   *
   * <b>Read from the report rather than tracked alongside it</b>, because the report already knows:
   * a dry run comes back <c>dryRun: true</c> and an apply does not. Keeping a second flag in step
   * with it would be one more thing to forget.
   */
  const applied = report !== null && !report.dryRun

  async function onFile(file: File) {
    setReport(null)
    setError(null)

    try {
      const text = await file.text()
      const bundle: unknown = JSON.parse(text)
      const version = (bundle as { formatVersion?: unknown }).formatVersion

      setLoaded({
        filename: file.name,
        bundle,
        formatVersion: typeof version === 'number' ? version : 0,
        summary: describe(bundle),
      })
    } catch (e: unknown) {
      // Parsed here rather than posted blindly, so a malformed file reports the position it went
      // wrong at instead of arriving as a server 400 that says only "unreadable body".
      setLoaded(null)
      toast.notify(e instanceof Error ? `That file is not JSON: ${e.message}` : 'Unreadable file.', 'bad')
    }
  }

  async function run(dryRun: boolean) {
    if (!loaded) return

    setBusy(true)
    setError(null)
    try {
      const result = await builderApi.importBundle(loaded.bundle, dryRun)
      setReport(result)

      if (!dryRun) {
        setConfirming(false)
        toast.notify(result.ok ? 'Import applied.' : 'Import applied in part — see the failures.', result.ok ? 'good' : 'bad')
        onImported()
      }
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : 'The import was refused.'
      setError(message)
      toast.notify(message, 'bad')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel setup-panel">
      <div className="setup-head">
        <h3>Export</h3>
      </div>

      <p className="dim">
        The authored world as one JSON document. Leave both boxes empty for everything; a zone
        bundle carries the world and zone above it, plus every template its content needs.
      </p>

      <Field label="World" hint="Optional. One world and all its zones.">
        <input
          value={scope.world}
          spellCheck={false}
          placeholder="ossara"
          onChange={(e) => setScope({ world: e.target.value, zone: '' })}
        />
      </Field>

      <Field label="Zone" hint="Optional, and wins over World when both are given.">
        <input
          value={scope.zone}
          spellCheck={false}
          placeholder="ossara.gatetown"
          onChange={(e) => setScope({ ...scope, zone: e.target.value })}
        />
      </Field>

      <div className="spawner-actions">
        {/* A real link, so the browser honours the attachment filename the server sends. */}
        <a
          className="setup-download"
          href={builderApi.exportUrl({
            world: scope.world.trim() || undefined,
            zone: scope.zone.trim() || undefined,
          })}
        >
          Download bundle
        </a>

        {/* Its own button rather than a third box, because it answers a different question. The
            boxes narrow a bundle to a place; an ability has no place, so there is nothing above it
            to name. */}
        <a
          className="setup-download"
          href={builderApi.exportUrl({ only: 'abilities' })}
        >
          Download abilities only
        </a>
      </div>

      <p className="dim">
        <strong>Abilities only</strong> ignores both boxes and is the file to save over{' '}
        <code>content/abilities.json</code>. A retune made in the editor lives in this
        server&rsquo;s database and nowhere else until it does — the next fresh install seeds from
        the file, not from here.
      </p>

      <div className="setup-head">
        <h3>Import</h3>
      </div>

      <p className="dim">
        An import is a <strong>merge</strong>: keys in the file are written, and anything this
        server has that the file does not is left alone. Removing something from a file does not
        remove it from the world.
      </p>

      {/* Shown whether or not a file is loaded, because "which version does this server take" is a
          question worth being able to answer while looking at a deployment rather than only while
          holding a bundle. */}
      <p className="dim">
        {serverVersion === null
          ? 'This server has not said which bundle format it reads.'
          : `This server reads bundle format ${serverVersion}.`}
      </p>

      <Field label="Bundle file">
        <input
          ref={fileInput}
          type="file"
          accept="application/json,.json"
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) void onFile(file)
          }}
        />
      </Field>

      {loaded && (
        <div className="section-body">
          <p>
            <strong>{loaded.filename}</strong>
            <span className="dim"> · format {loaded.formatVersion} · {loaded.summary}</span>
          </p>

          {/* Said here rather than discovered by uploading. The server refuses this anyway, so this
              is not the check — it is the check arriving early enough to be useful. */}
          {mismatch && <p className="bad">{mismatch}</p>}
          {error && <p className="bad">{error}</p>}

          {applied ? (
            <>
              {/* The panel used to come back to exactly the state it was in before, with Apply
                  still lit - the only thing that changed was one word in a heading further down.
                  It read as though nothing had happened, which for a write to the live world is
                  the wrong thing for a screen to say. */}
              <p className="good">
                <strong>{loaded.filename}</strong> has been applied to this server.
              </p>

              <div className="spawner-actions">
                <Button variant="primary" onClick={reset}>
                  Import another file
                </Button>
              </div>

              <p className="dim">
                Applying it again would write every entity in it a second time. Load the file afresh
                and dry run it if that is what you mean to do.
              </p>
            </>
          ) : (
            <>
              <div className="spawner-actions">
                <Button variant="primary" disabled={busy || mismatch !== null} onClick={() => void run(true)}>
                  {busy ? 'Checking…' : 'Dry run'}
                </Button>

                {/* Only after a rehearsal, and only if it came back clean enough to read. Applying
                    first and reading the report afterwards is the order that leaves a half-applied
                    world behind.

                    Gated on the report being a *dry run*, not merely on there being one. An apply
                    produces a report too, so `report === null` was satisfied by the apply itself
                    and the button came straight back - one click from writing the whole
                    non-atomic bundle a second time. */}
                <Button
                  disabled={busy || report === null || !report.dryRun || mismatch !== null}
                  onClick={() => setConfirming(true)}
                >
                  Apply
                </Button>

                <Button onClick={reset}>Clear</Button>
              </div>

              {report === null && mismatch === null && (
                <p className="dim">Dry run first — it reports what would happen and changes nothing.</p>
              )}
            </>
          )}
        </div>
      )}

      {report && (
        <div className="section-body">
          <h4>{report.dryRun ? 'Dry run' : 'Applied'}</h4>

          <ul className="setup-list">
            {report.counts
              .filter((c) => c.created > 0 || c.updated > 0 || c.removed > 0)
              .map((c) => (
                <li key={c.kind}>
                  <code>{c.kind}</code>
                  <span className="dim">
                    {' '}
                    · {c.created} new, {c.updated} updated
                  </span>
                  {/* Only when there are any, and not dimmed: a deletion is the one number here
                      that cannot be undone by importing again, so it does not get to look like
                      the other two. Each one is named in the warnings below. */}
                  {c.removed > 0 && (
                    <span className="bad">
                      {' '}
                      · {c.removed} removed
                    </span>
                  )}
                </li>
              ))}
          </ul>

          {report.counts.every((c) => c.created === 0 && c.updated === 0 && c.removed === 0) && (
            <p className="dim">Nothing to write — this server already matches the file.</p>
          )}

          {report.warnings.length > 0 && (
            <>
              <h4>Warnings</h4>
              {/* Advisory by design (§7.4): a zone imported ahead of the zone it links to is a
                  state the world tolerates, so these never block. */}
              <ul className="setup-list">
                {report.warnings.map((w, i) => (
                  <li key={`${w.kind}-${w.entityKey}-${i}`} className="dim">
                    <code>{w.entityKey}</code> — {w.message}
                  </li>
                ))}
              </ul>
            </>
          )}

          {report.failures.length > 0 && (
            <>
              <h4 className="bad">Failures</h4>
              <ul className="setup-list">
                {report.failures.map((f, i) => (
                  <li key={`${f.kind}-${f.key}-${i}`} className="bad">
                    <code>{f.key}</code> — {f.message}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title="Apply this bundle?"
        description={
          <>
            This writes to the live world and players standing in these rooms will see the change.
            An import is <strong>not atomic</strong> — if it fails part way through, everything
            before that point stays applied.
            {/* Said in the dialog as well as in the panel behind it, because this is the one place
                the dialog stays open after a failed run - and a re-enabled Apply with no reason
                beside it reads as the import having quietly done nothing. */}
            {error && (
              <>
                {' '}
                <span className="bad">The last attempt was refused: {error}</span>
              </>
            )}
          </>
        }
        confirmLabel="Apply"
        busy={busy}
        onConfirm={() => void run(false)}
      />
    </section>
  )
}

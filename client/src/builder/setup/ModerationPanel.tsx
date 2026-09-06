import { useCallback, useEffect, useState } from 'react'
import { builderApi } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'

/**
 * The server's blocked-words list.
 *
 * Its own panel rather than a field on a starter configuration, which is where it used to live.
 * A configuration says which world a new character wakes up in; this says what the people playing
 * here may not call each other. Storing them together meant a server with two configurations had
 * two lists and activating a different realm silently swapped the moderation policy — which nobody
 * chose, and which would surface as a filter that stopped working for reasons no one could connect
 * to the world they had just loaded.
 *
 * The engine ships a default that is seeded once, on a server that has never had a list. After
 * that this screen is the authority, and saving takes effect on the next line somebody types
 * rather than on the next deploy.
 */
export function ModerationPanel() {
  const [words, setWords] = useState('')
  const [loaded, setLoaded] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  const load = useCallback(async () => {
    try {
      const policy = await builderApi.moderation()
      setWords(policy.blockedWords)
      setLoaded(policy.blockedWords)
      setError(null)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const dirty = loaded !== null && words !== loaded

  async function save() {
    setBusy(true)
    setSaved(false)
    try {
      const policy = await builderApi.saveModeration(words)
      setWords(policy.blockedWords)
      setLoaded(policy.blockedWords)
      setError(null)
      setSaved(true)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel setup-panel">
      <div className="setup-head">
        <h3>Moderation</h3>
      </div>

      {error && <p className="bad">{error}</p>}

      <Field
        label="Blocked words"
        hint={
          <>
            Words nobody may say on this server, one per line. Whole words only, any case: an entry
            refuses the word on its own and nothing that merely contains it, so listing “ass” does
            not refuse “class”. Leave it empty for no filter. Applies to speech, tells, chat,
            emotes, party chat and new character names, and takes effect as soon as it is saved —
            it does not wait for a configuration to be activated, and switching worlds does not
            change it.
          </>
        }
      >
        <Textarea rows={8} maxRows={20} value={words} onChange={setWords} />
      </Field>

      <div className="row">
        <button type="button" onClick={() => void save()} disabled={busy || !dirty}>
          {busy ? 'Saving…' : 'Save list'}
        </button>
        {saved && !dirty && <span className="dim">Saved. Live now.</span>}
      </div>
    </section>
  )
}

import { useEffect, useState } from 'react'
import {
  builderApi,
  CHARACTER_PATHS,
  type CharacterPath,
  type Quest,
  type ReachabilityWarning,
} from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { ProseAssist } from '../assist/ProseAssist'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { NumberInput } from '../../ui/NumberInput'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'
import { useBuilderData } from '../BuilderData'
import { TemplatePicker } from '../templates/TemplatePicker'
import { ReachabilityPanel } from './ReachabilityPanel'
import { DIALOGUE_FIELDS, formatKeyList, parseKeyList } from './quests'

interface Props {
  questKey: string
  onChanged: (quest: Quest) => void
  onDeleted: (key: string) => void
}

/**
 * The quest editor (PLAN.md §4.9, §7.3).
 *
 * 5.2b was checked off naming this file, and the file did not exist — so the engine, the CRUD
 * API, reachability, and the storyline graph were all complete and tested while a quest could
 * only be authored by hand against the API. Every mob and item reference is picked from a real
 * template rather than typed, because a typo produces a *dormant* quest: one that reads perfectly
 * in the journal, is offered by nobody, and reports no error anywhere.
 */
export function QuestEditor({ questKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const { mobTemplates, itemTemplates } = useBuilderData()

  const [quest, setQuest] = useState<Quest | null>(null)
  const [name, setName] = useState('')
  const [summary, setSummary] = useState('')
  const [description, setDescription] = useState('')
  const [giverMobKey, setGiverMobKey] = useState('')
  const [turninMobKey, setTurninMobKey] = useState('')
  const [requiredItemKey, setRequiredItemKey] = useState('')
  const [requiredCount, setRequiredCount] = useState(1)
  const [rewardXp, setRewardXp] = useState(0)
  const [rewardGold, setRewardGold] = useState(0)
  const [rewardItemKey, setRewardItemKey] = useState('')
  const [rewardItemCount, setRewardItemCount] = useState(1)
  const [rewardFlagKey, setRewardFlagKey] = useState('')
  const [prerequisites, setPrerequisites] = useState('')
  const [isRepeatable, setIsRepeatable] = useState(false)
  const [autoStart, setAutoStart] = useState(false)
  const [paths, setPaths] = useState<CharacterPath[]>([])
  const [dialogue, setDialogue] = useState<Record<string, string>>({})
  const [sortOrder, setSortOrder] = useState(0)

  const [warnings, setWarnings] = useState<ReachabilityWarning[]>([])
  const [checking, setChecking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    let cancelled = false
    setQuest(null)
    setError(null)

    void builderApi
      .quest(questKey)
      .then((loaded) => {
        if (cancelled) return
        setQuest(loaded)
        setName(loaded.name)
        setSummary(loaded.summary)
        setDescription(loaded.description)
        setGiverMobKey(loaded.giverMobKey)
        setTurninMobKey(loaded.turninMobKey)
        setRequiredItemKey(loaded.requiredItemKey ?? '')
        setRequiredCount(loaded.requiredCount)
        setRewardXp(loaded.rewardXp)
        setRewardGold(loaded.rewardGold)
        setRewardItemKey(loaded.rewardItemKey ?? '')
        setRewardItemCount(loaded.rewardItemCount)
        setRewardFlagKey(loaded.rewardFlagKey ?? '')
        setPrerequisites(formatKeyList(loaded.prerequisiteQuestKeys))
        setIsRepeatable(loaded.isRepeatable)
        setAutoStart(loaded.autoStart)
        // Ordered to CHARACTER_PATHS rather than to whatever came back, so the checkboxes and the
        // saved list agree and a save with nothing changed is not a change.
        setPaths(CHARACTER_PATHS.filter((p) => (loaded.paths ?? []).includes(p)))
        setDialogue({ ...loaded.dialogue })
        setSortOrder(loaded.sortOrder)
        setDirty(false)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load that quest.')
      })

    return () => {
      cancelled = true
    }
  }, [questKey])

  // Reachability is asked of the *saved* quest, so it answers about what a player would meet
  // rather than about an unsaved draft. Re-run after each save.
  useEffect(() => {
    if (!quest) return

    let cancelled = false
    setChecking(true)
    void builderApi
      .questReachability(questKey)
      .then((result) => {
        if (!cancelled) setWarnings(result.warnings)
      })
      .catch(() => {
        if (!cancelled) setWarnings([])
      })
      .finally(() => {
        if (!cancelled) setChecking(false)
      })

    return () => {
      cancelled = true
    }
  }, [questKey, quest])

  async function save() {
    setBusy(true)
    setError(null)
    try {
      const updated = await builderApi.updateQuest(questKey, {
        name,
        summary,
        description,
        giverMobKey,
        turninMobKey,
        // Empty means *no* required item - a talk-to quest - which is not the same as an empty
        // string, and the column is nullable for exactly that reason.
        requiredItemKey: requiredItemKey.trim() === '' ? null : requiredItemKey.trim(),
        requiredCount,
        rewardXp,
        rewardGold,
        rewardItemKey: rewardItemKey.trim() === '' ? null : rewardItemKey.trim(),
        rewardItemCount,
        rewardFlagKey: rewardFlagKey.trim() === '' ? null : rewardFlagKey.trim(),
        prerequisiteQuestKeys: parseKeyList(prerequisites),
        isRepeatable,
        autoStart,
        paths,
        dialogue: strippedDialogue(),
        sortOrder,
      })
      setQuest(updated)
      setDirty(false)
      onChanged(updated)
      toast.notify('Quest saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  /**
   * Blank lines are removed rather than stored as empty strings. The engine falls back to
   * generated prose only when the key is *absent*, so storing "" would make the NPC say nothing
   * at all at that moment.
   */
  function strippedDialogue(): Record<string, string> {
    return Object.fromEntries(
      Object.entries(dialogue).filter(([, value]) => value.trim() !== ''),
    )
  }

  async function confirmDelete() {
    setBusy(true)
    try {
      await builderApi.deleteQuest(questKey)
      toast.notify('Quest deleted')
      setDeleting(false)
      onDeleted(questKey)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  const touch = () => setDirty(true)

  const setLine = (key: string, value: string) => {
    setDialogue((current) => ({ ...current, [key]: value }))
    touch()
  }

  const mobOptions = mobTemplates.map((t) => ({ key: t.key, name: t.name || t.key }))
  const itemOptions = itemTemplates.map((t) => ({ key: t.key, name: t.name || t.key }))

  if (error && !quest) return <p className="bad">{error}</p>
  if (!quest) return <p className="dim">Loading…</p>

  return (
    <div className="template-editor">
      <header className="room-editor-head">
        <div>
          <h2>{name || questKey}</h2>
          <code className="room-key">{questKey}</code>
          <span className="dim"> · {quest.zoneKey}</span>
        </div>
        <OverflowMenu
          actions={[
            { label: 'Delete quest…', onSelect: () => setDeleting(true), destructive: true },
          ]}
        />
      </header>

      {error && <p className="bad">{error}</p>}

      <fieldset className="subpanel">
        <legend>Finishable?</legend>
        <ReachabilityPanel warnings={warnings} checking={checking} />
      </fieldset>

      <Field label="Name">
        <input
          value={name}
          onChange={(e) => {
            setName(e.target.value)
            touch()
          }}
        />
      </Field>

      <Field label="Summary" hint="One line. Used in the journal and in the default offer text.">
        <input
          value={summary}
          onChange={(e) => {
            setSummary(e.target.value)
            touch()
          }}
        />
      </Field>

      <Field label="Description">
        <Textarea
          rows={3}
          value={description}
          onChange={(v) => {
            setDescription(v)
            touch()
          }}
        />
      </Field>

      <ProseAssist
        kind="Quest"
        entityKey={questKey}
        name={name}
        summary={summary}
        description={description}
        onName={(v) => {
          setName(v)
          touch()
        }}
        onSummary={(v) => {
          setSummary(v)
          touch()
        }}
        onDescription={(v) => {
          setDescription(v)
          touch()
        }}
      />

      <fieldset className="subpanel">
        <legend>Who</legend>
        <p className="dim">
          Both should be NPCs. A killable quest giver strands anyone mid-quest until it respawns,
          and a quest whose giver no longer exists goes dormant — still in the journal, offered by
          nobody.
        </p>

        <Field label="Giver" hint="Talk to this mob to be offered the quest.">
          <TemplatePicker
            value={giverMobKey}
            options={mobOptions}
            onChange={(key) => {
              setGiverMobKey(key)
              touch()
            }}
          />
        </Field>

        <Field label="Turn-in" hint="Give the item to this mob. Often the same as the giver.">
          <TemplatePicker
            value={turninMobKey}
            options={mobOptions}
            onChange={(key) => {
              setTurninMobKey(key)
              touch()
            }}
          />
        </Field>
      </fieldset>

      <fieldset className="subpanel">
        <legend>What it asks for</legend>
        <p className="dim">
          Leave the item empty for a talk-to quest. Mark the item as a quest item in its own
          editor so it cannot be sold or destroyed mid-quest.
        </p>

        <div className="field-row">
          <Field label="Required item">
            <TemplatePicker
              value={requiredItemKey}
              options={itemOptions}
              onChange={(key) => {
                setRequiredItemKey(key)
                touch()
              }}
            />
          </Field>

          <Field label="Count">
            <NumberInput
              min={1}
              value={requiredCount}
              onChange={(v) => {
                setRequiredCount(v)
                touch()
              }}
            />
          </Field>
        </div>
      </fieldset>

      <fieldset className="subpanel">
        <legend>Rewards</legend>
        <p className="dim">
          XP and gold are scaled by the zone's multipliers when they are paid out, so these are
          the numbers before difficulty.
        </p>

        <div className="field-row">
          <Field label="XP">
            <NumberInput
              min={0}
              value={rewardXp}
              onChange={(v) => {
                setRewardXp(v)
                touch()
              }}
            />
          </Field>

          <Field label="Gold">
            <NumberInput
              min={0}
              value={rewardGold}
              onChange={(v) => {
                setRewardGold(v)
                touch()
              }}
            />
          </Field>
        </div>

        <div className="field-row">
          <Field label="Reward item">
            <TemplatePicker
              value={rewardItemKey}
              options={itemOptions}
              onChange={(key) => {
                setRewardItemKey(key)
                touch()
              }}
            />
          </Field>

          <Field label="Count">
            <NumberInput
              min={1}
              value={rewardItemCount}
              onChange={(v) => {
                setRewardItemCount(v)
                touch()
              }}
            />
          </Field>
        </div>

        <Field
          label="Reward flag"
          hint="A capability the character keeps for good — what a gated exit asks for (PLAN.md §4.15). Free text: there is no list of flags, because which ones are real is up to the world you author."
        >
          <input
            value={rewardFlagKey}
            placeholder="attuned.grask"
            spellCheck={false}
            onChange={(e) => {
              setRewardFlagKey(e.target.value)
              touch()
            }}
          />
        </Field>
      </fieldset>

      <fieldset className="subpanel">
        <legend>Chain</legend>
        <p className="dim">
          Quest keys, comma separated. All of them must be completed before this one is offered.
          A key that does not exist yet is allowed — building a chain backwards is normal — and
          the chain panel reports one that never turns up.
        </p>

        <Field label="Prerequisites">
          <input
            value={prerequisites}
            spellCheck={false}
            placeholder="millbrook.first-errand, millbrook.the-ledger"
            onChange={(e) => {
              setPrerequisites(e.target.value)
              touch()
            }}
          />
        </Field>

        <label className="field-check">
          <input
            type="checkbox"
            checked={autoStart}
            disabled={prerequisites.trim() === ''}
            onChange={(e) => {
              setAutoStart(e.target.checked)
              touch()
            }}
          />
          Starts by itself when its prerequisites are done — no <code>talk</code> needed
        </label>

        <p className="dim">
          {prerequisites.trim() === ''
            ? 'Needs a prerequisite first: a quest with nothing in front of it has no moment to start at, so it always waits for a talk.'
            : autoStart
              ? 'Hand in the quest above and this one opens on the spot, with its own offer line — which reads best when the same NPC takes the turn-in and gives this one. It still refuses everything a talk would refuse, so it can never reach a state a player could not have reached by asking.'
              : 'The player has to seek out the giver and talk. Right for a chain that should send them somewhere.'}
        </p>

        <Field
          label="Paths"
          hint="None ticked means anyone may take it. Ticking any offers it only to those."
        >
          <div className="check-row">
            {CHARACTER_PATHS.map((path) => (
              <label className="field-check" key={path}>
                <input
                  type="checkbox"
                  checked={paths.includes(path)}
                  onChange={(e) => {
                    // Rebuilt from CHARACTER_PATHS rather than pushed onto, so the saved order is
                    // the enum's however they were ticked - the same reasoning as the item editor's.
                    setPaths(
                      CHARACTER_PATHS.filter((p) =>
                        p === path ? e.target.checked : paths.includes(p),
                      ),
                    )
                    touch()
                  }}
                />
                {path}
              </label>
            ))}
          </div>
        </Field>

        <p className="dim">
          {paths.length === 0
            ? 'Anyone may be offered this. Right for almost every quest.'
            : `Only a ${paths.join(' or ')} is offered this, and only they can hand it in. For a chain whose reward only one Path can use — the epic weapons are the reason this exists.`}
        </p>

        <label className="field-check">
          <input
            type="checkbox"
            checked={isRepeatable}
            onChange={(e) => {
              setIsRepeatable(e.target.checked)
              touch()
            }}
          />
          Repeatable — can be taken again once the whole chain is finished
        </label>

        <p className="dim">
          {isRepeatable
            ? 'Offered again only when nothing further down the chain is still open, and only after the step above it has been run again. A player part-way through has to finish or abandon first, so a repeat is always a fresh run rather than a re-entry into the middle.'
            : 'Once only. Finishing the rest of the chain does not reopen it.'}
        </p>

        <Field label="Sort order" hint="Lower sorts first in the journal.">
          <NumberInput
            min={0}
            value={sortOrder}
            onChange={(v) => {
              setSortOrder(v)
              touch()
            }}
          />
        </Field>
      </fieldset>

      <fieldset className="subpanel">
        <legend>Dialogue</legend>
        <p className="dim">
          Every line is optional. Left blank, the NPC says the generated line shown under each
          field — which is often fine, and always better than silence.
        </p>
        <p className="dim">
          In the offer, <code>&lt;angle brackets&gt;</code> mark the words a player clicks to take
          the quest on. Those words are also what they can type, so there is nothing else to keep
          in step — but two quests the same giver can offer at once must not mark the same words,
          and the import will say so.
        </p>

        {DIALOGUE_FIELDS.map((field) => (
          <Field key={field.key} label={field.label} hint={field.hint}>
            <Textarea
              rows={2}
              value={dialogue[field.key] ?? ''}
              placeholder={field.fallback}
              onChange={(v) => setLine(field.key, v)}
            />
          </Field>
        ))}
      </fieldset>

      <div className="field-row">
        <Button variant="primary" onClick={() => void save()} disabled={busy}>
          {busy ? 'Saving…' : 'Save'}
        </Button>
        {dirty && <span className="dim">Unsaved changes</span>}
      </div>

      {/* `description`, not children - ConfirmDialog renders no children, so a <p> here would
          vanish silently and the prompt would ask "delete?" with nothing to go on. */}
      <ConfirmDialog
        open={deleting}
        onOpenChange={setDeleting}
        title={`Delete ${questKey}?`}
        description="Characters part-way through it keep their progress row, which then refers to a quest that no longer exists."
        destructive
        busy={busy}
        confirmLabel="Delete quest"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}

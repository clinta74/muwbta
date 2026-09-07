import { useEffect, useState } from 'react'
import { builderApi, type MobAttack, type MobTemplate } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { ProseAssist } from '../assist/ProseAssist'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { NumberInput } from '../../ui/NumberInput'
import { SecondsInput } from '../../ui/SecondsInput'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'
import { useBuilderData } from '../BuilderData'
import { MobBehaviorEditor } from './MobBehaviorEditor'
import { readBehavior, writeBehavior, type BehaviorDraft } from './behavior'
import { MobStatsEditor } from './MobStatsEditor'
import { LootEditor } from './LootEditor'
import { readLoot, writeLoot, type LootRow } from './mobStats'
import { AttackEditor } from './AttackEditor'

interface Props {
  templateKey: string
  onChanged: (template: MobTemplate) => void
  onDeleted: (key: string) => void
}

export function MobTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const { itemTemplates } = useBuilderData()
  const [template, setTemplate] = useState<MobTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('m')
  const [level, setLevel] = useState(1)
  const [wanderIntervalPulses, setWanderIntervalPulses] = useState(24)
  const [baseStats, setBaseStats] = useState<Record<string, unknown>>({})
  const [loot, setLoot] = useState<LootRow[]>([])
  const [baseXp, setBaseXp] = useState(0)
  const [baseGold, setBaseGold] = useState(0)
  const [attacks, setAttacks] = useState<MobAttack[]>([])
  const [behavior, setBehavior] = useState<BehaviorDraft>(readBehavior(undefined))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    let cancelled = false
    setTemplate(null)
    setError(null)
    void builderApi
      .mobTemplate(templateKey)
      .then((loaded) => {
        if (cancelled) return
        setTemplate(loaded)
        setName(loaded.name)
        setDescription(loaded.description)
        setIcon(loaded.icon)
        setLevel(loaded.level)
        setWanderIntervalPulses(loaded.wanderIntervalPulses ?? 24)
        // Kept verbatim. Only the keys the form owns are written, in place - the item editor
        // learned this the hard way when a blanket Number() coercion zeroed every dice string.
        setBaseStats(
          loaded.baseStats && typeof loaded.baseStats === 'object' ? { ...loaded.baseStats } : {},
        )
        setLoot(readLoot(loaded.loot))
        setBaseXp(loaded.baseXp)
        setBaseGold(loaded.baseGold)
        setAttacks(loaded.attacks ?? [])
        setBehavior(readBehavior(loaded.behavior))
        setDirty(false)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load that template.')
      })
    return () => {
      cancelled = true
    }
  }, [templateKey])

  async function save() {
    if (!template) return
    setBusy(true)
    setError(null)
    try {
      const updated = await builderApi.updateMobTemplate(templateKey, {
        name,
        description,
        icon,
        level,
        wanderIntervalPulses,
        baseStats,
        baseXp,
        baseGold,
        loot: writeLoot(loot),
        // Folded into the stored bag rather than replacing it, so keys this form does not
        // render survive the save.
        behavior: writeBehavior(template.behavior, behavior),
        attacks,
      })
      setTemplate(updated)
      setDirty(false)
      onChanged(updated)
      toast.notify('Template saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  async function confirmDelete() {
    setBusy(true)
    try {
      await builderApi.deleteMobTemplate(templateKey)
      toast.notify('Template deleted')
      setDeleting(false)
      onDeleted(templateKey)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  const change = <T,>(setter: (v: T) => void) => (v: T) => {
    setter(v)
    setDirty(true)
  }

  if (error && !template) return <p className="bad">{error}</p>
  if (!template) return <p className="dim">Loading…</p>

  return (
    <div className="template-editor">
      <header className="room-editor-head">
        <div>
          <h2>{name || templateKey}</h2>
          <code className="room-key">{templateKey}</code>
        </div>
        <OverflowMenu
          actions={[{ label: 'Delete template…', onSelect: () => setDeleting(true), destructive: true }]}
        />
      </header>

      {error && <p className="bad">{error}</p>}

      {/* Who it is, on one line. The icon is one character and the level is two digits, and both
          used to be full-rail boxes on rows of their own — three lines to say what fits on one. */}
      <div className="field-row">
        <Field label="Icon" width="char">
          <input
            value={icon}
            maxLength={1}
            onChange={(e) => change(setIcon)(e.target.value.slice(0, 1) || 'm')}
          />
        </Field>
        <Field label="Name" width="lg">
          <input value={name} onChange={(e) => change(setName)(e.target.value)} />
        </Field>
        <Field label="Level" width="xs">
          <NumberInput min={1} value={level} onChange={change(setLevel)} />
        </Field>
      </div>

      <Field label="Description">
        <Textarea rows={3} value={description} onChange={change(setDescription)} />
      </Field>

      <ProseAssist
        kind="Mob"
        entityKey={templateKey}
        name={name}
        description={description}
        onName={change(setName)}
        onDescription={change(setDescription)}
      />

      {/* What killing it is worth, and how restless it is while alive. Three numbers, one line. */}
      <div className="field-row">
        <Field label="Wanders every" width="sm" hint="Seconds. Lower is more restless.">
          <SecondsInput
            minPulses={4}
            pulses={wanderIntervalPulses}
            onChange={change(setWanderIntervalPulses)}
          />
        </Field>
        <Field label="Base XP" width="xs">
          <NumberInput min={0} value={baseXp} onChange={change(setBaseXp)} />
        </Field>
        <Field label="Base gold" width="xs">
          <NumberInput min={0} value={baseGold} onChange={change(setBaseGold)} />
        </Field>
      </div>

      <MobStatsEditor
        stats={baseStats}
        onChange={(next) => {
          setBaseStats(next)
          setDirty(true)
        }}
      />

      <LootEditor
        rows={loot}
        itemTemplates={itemTemplates}
        onChange={(next) => {
          setLoot(next)
          setDirty(true)
        }}
      />

      <MobBehaviorEditor
        draft={behavior}
        itemTemplates={itemTemplates}
        onChange={(next) => {
          setBehavior(next)
          setDirty(true)
        }}
      />

      <AttackEditor
        attacks={attacks}
        onChange={(next) => {
          setAttacks(next)
          setDirty(true)
        }}
      />

      <div className="row">
        <Button variant="primary" disabled={!dirty || busy} onClick={() => void save()}>
          {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
        </Button>
      </div>

      <ConfirmDialog
        open={deleting}
        onOpenChange={setDeleting}
        title={`Delete ${templateKey}?`}
        description="This cannot be undone. Spawners referencing it will stop working."
        destructive
        busy={busy}
        confirmLabel="Delete template"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}

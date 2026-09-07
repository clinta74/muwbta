import { useEffect, useState } from 'react'
import { builderApi, type Multipliers } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { NumberInput } from '../../ui/NumberInput'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { Tabs } from '../../ui/Tabs'
import { Textarea } from '../../ui/Textarea'
import { useToast } from '../../ui/Toast'
import { useBuilderData } from '../BuilderData'
import { MultiplierEditor } from './MultiplierEditor'
import { readMultipliers } from './multipliers'
import { ScopedFlagList } from './ScopedFlagList'

interface Props {
  worldKey: string
  onDeleted: () => void
}

type Section = 'details' | 'flags' | 'difficulty'

/**
 * The world editor.
 *
 * There was no such thing before: a world's name, description, sort order, flags, and difficulty
 * could only be set by seeding or by SQL, and the delete route — which exists on the server and
 * in `builderApi` — had no caller anywhere in the UI.
 */
export function WorldPanel({ worldKey, onDeleted }: Props) {
  const toast = useToast()
  const { worlds, refreshWorlds } = useBuilderData()
  const world = worlds.find((w) => w.key === worldKey)

  const [section, setSection] = useState<Section>('details')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [sortOrder, setSortOrder] = useState(0)
  const [multipliers, setMultipliers] = useState<Multipliers>(() => readMultipliers(undefined))
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    if (!world) return
    setName(world.name)
    setDescription(world.description)
    setSortOrder(world.sortOrder)
    setMultipliers(readMultipliers(world.multipliers))
    setDirty(false)
  }, [world?.key, world])

  if (!world) return null

  const change = <T,>(setter: (value: T) => void) => (value: T) => {
    setter(value)
    setDirty(true)
  }

  async function save() {
    if (!world) return
    setBusy(true)
    setError(null)
    try {
      await builderApi.updateWorld(world.key, { name, description, sortOrder, multipliers })
      await refreshWorlds()
      setDirty(false)
      toast.notify('World saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  async function confirmDelete() {
    if (!world) return
    setBusy(true)
    try {
      await builderApi.deleteWorld(world.key)
      await refreshWorlds()
      setDeleting(false)
      toast.notify('World deleted')
      onDeleted()
    } catch (e) {
      // The server refuses while anyone is standing in it (§7.4), which is a message worth
      // showing rather than a failure to swallow.
      setError(e instanceof Error ? e.message : 'Delete failed.')
      setDeleting(false)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="editor-section">
      <header className="room-editor-head">
        <div>
          <h3>{name || world.key}</h3>
          <code className="room-key">{world.key}</code>
        </div>
        <OverflowMenu
          actions={[{ label: 'Delete world…', onSelect: () => setDeleting(true), destructive: true }]}
        />
      </header>

      {error && <p className="bad">{error}</p>}

      <Tabs
        value={section}
        onValueChange={(v) => setSection(v as Section)}
        aria-label="World sections"
        tabs={[
          { value: 'details', label: 'Details' },
          { value: 'flags', label: 'Flags' },
          { value: 'difficulty', label: 'Difficulty' },
        ]}
      />

      {section === 'details' && (
        <div className="section-body">
          <Field label="Name">
            <input value={name} onChange={(e) => change(setName)(e.target.value)} />
          </Field>

          <Field label="Description">
            <Textarea rows={3} value={description} onChange={change(setDescription)} />
          </Field>

          <Field label="Sort order" width="xs" hint="Lower sorts first in the world list.">
            <NumberInput min={0} value={sortOrder} onChange={change(setSortOrder)} />
          </Field>
        </div>
      )}

      {section === 'flags' && (
        <ScopedFlagList
          scope="world"
          flags={world.flags}
          inheritedNote="World flags sit at the top of the chain — a change here can flip a flag for every room in the world at once."
          onSet={(key, value) =>
            builderApi.setWorldFlag(world.key, key, value).then(() => refreshWorlds())
          }
        />
      )}

      {section === 'difficulty' && (
        <div className="section-body">
          <MultiplierEditor
            scope="world"
            value={multipliers}
            onChange={change(setMultipliers)}
            disabled={busy}
          />
          <p className="dim detail">
            Per-zone previews live on each zone, since what a number means depends on the zone it
            is multiplied into.
          </p>
        </div>
      )}

      {section !== 'flags' && (
        <div className="row">
          <Button
           
            variant="primary"
            disabled={!dirty || busy}
            onClick={() => void save()}
          >
            {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
          </Button>
        </div>
      )}

      <ConfirmDialog
        open={deleting}
        onOpenChange={setDeleting}
        title={`Delete ${world.key}?`}
        description="Every zone and room in it goes too. This cannot be undone, and is refused while anyone is standing in the world."
        destructive
        busy={busy}
        confirmLabel="Delete world"
        onConfirm={() => void confirmDelete()}
      />
    </section>
  )
}

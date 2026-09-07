import { useEffect, useState } from 'react'
import { builderApi, type Multipliers } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { NumberInput } from '../../ui/NumberInput'
import { Textarea } from '../../ui/Textarea'
import { Tabs } from '../../ui/Tabs'
import { useToast } from '../../ui/Toast'
import { useBuilderData } from '../BuilderData'
import { MultiplierEditor } from './MultiplierEditor'
import { readMultipliers } from './multipliers'
import { MultiplierPreviewPanel } from './MultiplierPreviewPanel'
import { ScopedFlagList } from './ScopedFlagList'

interface ZonePanelProps {
  zoneKey: string
}

type Section = 'details' | 'flags' | 'difficulty'

/**
 * The zone editor.
 *
 * It used to be two hardcoded buttons — `pvp` and `peaceful` — so a newly registered zone flag
 * was invisible, and a zone's name, level range, and difficulty could not be edited at all.
 * Flags now render from the same server registry the room editor reads.
 */
export function ZonePanel({ zoneKey }: ZonePanelProps) {
  const toast = useToast()
  const { zones, loadZones } = useBuilderData()
  const zone = zones.find((z) => z.key === zoneKey)

  const [section, setSection] = useState<Section>('details')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [minLevel, setMinLevel] = useState(1)
  const [maxLevel, setMaxLevel] = useState(50)
  const [multipliers, setMultipliers] = useState<Multipliers>(() => readMultipliers(undefined))
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [respawning, setRespawning] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [savedAt, setSavedAt] = useState(0)

  // Reload the form whenever a different zone is selected, or the selected one changes under us.
  useEffect(() => {
    if (!zone) return
    setName(zone.name)
    setDescription(zone.description)
    setMinLevel(zone.minLevel)
    setMaxLevel(zone.maxLevel)
    setMultipliers(readMultipliers(zone.multipliers))
    setDirty(false)
  }, [zone?.key, zone])

  if (!zone) return null

  const change = <T,>(setter: (value: T) => void) => (value: T) => {
    setter(value)
    setDirty(true)
  }

  async function save() {
    if (!zone) return
    setBusy(true)
    setError(null)
    try {
      await builderApi.updateZone(zone.key, {
        name,
        description,
        minLevel,
        maxLevel,
        multipliers,
      })
      await loadZones(zone.worldKey)
      setDirty(false)
      setSavedAt((n) => n + 1)
      toast.notify('Zone saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  /**
   * PLAN.md §7.5. The counts are reported rather than swallowed: a respawn that placed nothing
   * means the zone has no mob spawners, and "Respawned" alone would leave a builder waiting for a
   * change that was never going to come.
   */
  async function respawn() {
    if (!zone) return
    setRespawning(true)
    setError(null)
    try {
      const moved = await builderApi.respawnZone(zone.key)
      toast.notify(
        moved.spawned === 0 && moved.despawned === 0
          ? 'Nothing to respawn — this zone has no mob spawners.'
          : `Respawned ${moved.spawned} (${moved.despawned} removed)`,
      )
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Respawn failed.')
    } finally {
      setRespawning(false)
    }
  }

  return (
    <section className="editor-section">
      <header className="room-editor-head">
        <div>
          <h3>{name || zone.key}</h3>
          <code className="room-key">{zone.key}</code>
        </div>
      </header>

      {error && <p className="bad">{error}</p>}

      <Tabs
        value={section}
        onValueChange={(v) => setSection(v as Section)}
        aria-label="Zone sections"
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

          <div className="field-row">
            <Field label="Min level" width="xs">
              <NumberInput min={1} value={minLevel} onChange={change(setMinLevel)} />
            </Field>
            <Field label="Max level" width="xs" hint="Advisory — nothing enforces it yet.">
              <NumberInput min={1} value={maxLevel} onChange={change(setMaxLevel)} />
            </Field>
          </div>
        </div>
      )}

      {section === 'flags' && (
        <ScopedFlagList
          scope="zone"
          flags={zone.flags}
          inheritedNote="Unset flags fall through to the world, then to the registry default."
          onSet={(key, value) =>
            builderApi.setZoneFlag(zone.key, key, value).then(() => loadZones(zone.worldKey))
          }
        />
      )}

      {section === 'difficulty' && (
        <div className="section-body">
          <MultiplierEditor
            scope="zone"
            value={multipliers}
            onChange={change(setMultipliers)}
            disabled={busy}
          />

          <h4>Preview</h4>
          <p className="dim detail">
            Every template that spawns in this zone, at the numbers it will spawn with. Save to
            refresh.
          </p>
          <MultiplierPreviewPanel zoneKey={zone.key} refreshToken={savedAt} />

          <h4>Living mobs</h4>
          <p className="dim detail">
            Multipliers resolve when a mob spawns, so everything already standing in the zone keeps
            the numbers it was born with. Respawning clears out what this zone&rsquo;s mob spawners
            placed and fills every one of them again at once — hand-placed mobs and anything on the
            floor are left alone.
          </p>
          <div className="row">
            <Button
              disabled={busy || respawning || dirty}
              title={dirty ? 'Save first — a respawn applies the numbers on the server.' : undefined}
              onClick={() => void respawn()}
            >
              {respawning ? 'Respawning…' : 'Respawn zone'}
            </Button>
          </div>
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
    </section>
  )
}

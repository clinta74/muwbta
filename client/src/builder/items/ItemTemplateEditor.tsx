import { useEffect, useState } from 'react'
import {
  builderApi,
  CHARACTER_PATHS,
  ITEM_SLOTS,
  type CharacterPath,
  type ItemSlot,
  type ItemTemplate,
} from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { ProseAssist } from '../assist/ProseAssist'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { NumberInput } from '../../ui/NumberInput'
import { OptionalSecondsInput } from '../../ui/SecondsInput'
import { NumberField } from '../../ui/NumberField'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'
import { asNumber, OWNED_STAT_KEYS, STAT_GROUPS } from './stats'

interface Props {
  templateKey: string
  onChanged: (template: ItemTemplate) => void
  onDeleted: (key: string) => void
}

export function ItemTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const [template, setTemplate] = useState<ItemTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('i')
  const [slots, setSlots] = useState<ItemSlot[]>([])
  const [isTwoHanded, setIsTwoHanded] = useState(false)
  const [weight, setWeight] = useState(0)
  const [baseValue, setBaseValue] = useState(0)
  // Deliberately `unknown`, not `number`. This bag is schemaless (PLAN.md §4.8) and carries
  // values this form does not render - `damage: "1d6"` most of all. It used to be loaded through
  // `Number(v) || 0`, which turned every one of them into 0 and saved that back, so editing an
  // item's *name* silently destroyed its damage dice.
  const [baseStats, setBaseStats] = useState<Record<string, unknown>>({})
  // Weapon speed and verb are columns, not base stats: the coercion below would turn a verb
  // into 0, and a delay below the floor has to be refusable by the server.
  const [attackDelayPulses, setAttackDelayPulses] = useState<number | null>(null)
  const [attackVerb, setAttackVerb] = useState('')
  const [isQuestItem, setIsQuestItem] = useState(false)
  const [isLore, setIsLore] = useState(false)
  const [isNoDrop, setIsNoDrop] = useState(false)
  const [isLightSource, setIsLightSource] = useState(false)
  const [foodValue, setFoodValue] = useState('')
  const [drinkValue, setDrinkValue] = useState('')
  const [paths, setPaths] = useState<CharacterPath[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    let cancelled = false
    setTemplate(null)
    setError(null)
    void builderApi
      .itemTemplate(templateKey)
      .then((loaded) => {
        if (cancelled) return
        setTemplate(loaded)
        setName(loaded.name)
        setDescription(loaded.description)
        setIcon(loaded.icon)
        // Ordered to ITEM_SLOTS for the same reason paths are ordered to CHARACTER_PATHS:
        // the boxes and the saved list have to agree, or an untouched form saves a change.
        setSlots(ITEM_SLOTS.filter((sl) => (loaded.slots ?? []).includes(sl)))
        setIsTwoHanded(loaded.isTwoHanded)
        setWeight(loaded.weight)
        setBaseValue(loaded.baseValue)
        // Kept verbatim. Only the keys this form owns are ever written, in place.
        setBaseStats(
          loaded.baseStats && typeof loaded.baseStats === 'object' ? { ...loaded.baseStats } : {},
        )
        setAttackDelayPulses(loaded.attackDelayPulses ?? null)
        setAttackVerb(loaded.attackVerb ?? '')
        setIsQuestItem(loaded.isQuestItem)
        setIsLore(loaded.isLore)
        setIsNoDrop(loaded.isNoDrop)
        setIsLightSource(loaded.isLightSource)
        setFoodValue(loaded.foodValue?.toString() ?? '')
        setDrinkValue(loaded.drinkValue?.toString() ?? '')
        // Ordered to CHARACTER_PATHS rather than to whatever came back, so the checkboxes and the
        // saved list agree and a save with nothing changed is not a change.
        setPaths(CHARACTER_PATHS.filter((p) => (loaded.paths ?? []).includes(p)))
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
    setBusy(true)
    setError(null)
    try {
      const updated = await builderApi.updateItemTemplate(templateKey, {
        name,
        description,
        icon,
        slots,
        isTwoHanded,
        weight,
        baseValue,
        baseStats,
        attackDelayPulses,
        attackVerb: attackVerb.trim() === '' ? null : attackVerb.trim(),
        isQuestItem,
        isLore,
        isNoDrop,
        isLightSource,
        // Blank means "not food", which is null rather than 0 - the server tells the two
        // apart and `eat` refuses anything without a value.
        foodValue: foodValue.trim() === '' ? null : Number(foodValue),
        drinkValue: drinkValue.trim() === '' ? null : Number(drinkValue),
        paths,
      })
      setTemplate(updated)
      setSlots(ITEM_SLOTS.filter((sl) => updated.slots.includes(sl)))
      setIsTwoHanded(updated.isTwoHanded)
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
      await builderApi.deleteItemTemplate(templateKey)
      toast.notify('Template deleted')
      setDeleting(false)
      onDeleted(templateKey)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  const touch = () => setDirty(true)

  // Everything in the bag this form has no field for. Sorted so the list does not reshuffle
  // between renders.
  const carriedStats = Object.entries(baseStats)
    .filter(([key]) => !OWNED_STAT_KEYS.includes(key))
    .sort(([a], [b]) => a.localeCompare(b))

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

      {/* The icon holds one character. It was alone on a `.field-row` that had no rule behind it,
          so it rendered as a block and the global `input { width: 100% }` gave a single glyph the
          full width of the rail — with the name it belongs beside stacked above it. */}
      <div className="field-row">
        <Field label="Icon" width="char">
          <input
            value={icon}
            maxLength={1}
            onChange={(e) => {
              setIcon(e.target.value.slice(0, 1) || 'i')
              touch()
            }}
          />
        </Field>

        <Field label="Name" width="lg">
          <input
            value={name}
            onChange={(e) => {
              setName(e.target.value)
              touch()
            }}
          />
        </Field>
      </div>

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
        kind="Item"
        entityKey={templateKey}
        name={name}
        description={description}
        onName={(v) => {
          setName(v)
          touch()
        }}
        onDescription={(v) => {
          setDescription(v)
          touch()
        }}
      />

      <fieldset className="subpanel">
        <legend>Slots</legend>
        <p className="dim detail">
          Where it can be equipped. Tick more than one to let it go in any of them — a blade set to
          both hands is wielded in the main hand when that is free, and the off hand otherwise.
          Nothing ticked is a ground item.
        </p>

        <div className="check-row">
          {ITEM_SLOTS.map((s) => (
            <label className="field-check" key={s}>
              <input
                type="checkbox"
                checked={slots.includes(s)}
                onChange={(e) => {
                  // Rebuilt from ITEM_SLOTS rather than pushed onto, so the saved order is the
                  // server's enum order however they were ticked - and that order is what decides
                  // which hand is reached for first.
                  setSlots(
                    ITEM_SLOTS.filter((other) =>
                      other === s ? e.target.checked : slots.includes(other),
                    ),
                  )
                  touch()
                }}
              />
              {s}
            </label>
          ))}
        </div>

        <label className="field-check">
          <input
            type="checkbox"
            checked={isTwoHanded}
            onChange={(e) => {
              setIsTwoHanded(e.target.checked)
              touch()
            }}
          />
          Takes both hands
        </label>

        {isTwoHanded && (slots.length !== 1 || slots[0] !== 'MainHand') && (
          /* Said here rather than only on save, because the server refuses this outright and a
             400 after typing a description is a worse way to learn it. A two-handed item claims
             the off hand, so it cannot also be something that goes there. */
          <p className="dim detail">
            A two-handed item must be main hand and nothing else — it claims the off hand rather
            than filling it. This will be refused on save.
          </p>
        )}

        {isTwoHanded && slots.length === 1 && slots[0] === 'MainHand' && (
          <p className="dim detail">
            Nothing can be wielded in the off hand while this is held — no shield, no torch, no
            second weapon.
          </p>
        )}
      </fieldset>

      <div className="field-row">
        <Field label="Weight" width="xs" hint="Grams.">
          <NumberInput
            min={0}
            value={weight}
            onChange={(v) => {
              setWeight(v)
              touch()
            }}
          />
        </Field>

        <Field label="Base value" width="xs">
          <NumberInput
            min={0}
            value={baseValue}
            onChange={(v) => {
              setBaseValue(v)
              touch()
            }}
          />
        </Field>
      </div>

      <fieldset className="subpanel">
        <legend>Swing timing</legend>
        <p className="dim detail">
          Blank speed means this is not a weapon: in a main hand it swings at the default 8
          pulses, in an off hand it never strikes at all.
        </p>
        <div className="field-row">
          {/* OptionalSecondsInput rather than a hand-rolled field, because blank is a real value
              here - it means the weapon declares no speed of its own - and because the hand-rolled
              version could not accept a decimal at all: it round-tripped every keystroke through
              toPulses, so the point in "1." was erased as it was typed and 1.5 was unreachable. */}
          <Field label="Attack delay" width="sm" hint="Seconds, minimum 1.">
            <OptionalSecondsInput
              pulses={attackDelayPulses}
              minPulses={4}
              aria-label="Attack delay (seconds)"
              onChange={(pulses) => {
                setAttackDelayPulses(pulses)
                touch()
              }}
            />
          </Field>
          <Field label="Attack verb" width="md" hint="Base form: slash, crush, stab.">
            <input
              value={attackVerb}
              maxLength={24}
              onChange={(e) => {
                setAttackVerb(e.target.value)
                touch()
              }}
            />
          </Field>
        </div>
      </fieldset>

      <fieldset className="subpanel">
        <legend>Restrictions</legend>
        <p className="dim detail">
          Every one of these is off by default, and an item stays unrestricted until you say
          otherwise.
        </p>

        <label className="field-check">
          <input
            type="checkbox"
            checked={isQuestItem}
            onChange={(e) => {
              setIsQuestItem(e.target.checked)
              touch()
            }}
          />
          Quest item — cannot be sold or destroyed, but can still be dropped
        </label>
        {isQuestItem && (
          <p className="dim detail">
            {/* The one of the four that behaves differently, and the difference is worth stating
                where a builder is deciding: quest-ness is a protection stamped onto the copy, so
                it must survive this box being unticked. The three below are restrictions read
                live from the template, so widening them frees copies already in packs. */}
            Stamped onto each copy when it spawns, so items already in a pack keep the rule they
            were created under. Turning this off will not free copies that already exist.
          </p>
        )}

        <label className="field-check">
          <input
            type="checkbox"
            checked={isLore}
            onChange={(e) => {
              setIsLore(e.target.checked)
              touch()
            }}
          />
          Lore — one only, counting what is worn
        </label>

        <label className="field-check">
          <input
            type="checkbox"
            checked={isNoDrop}
            onChange={(e) => {
              setIsNoDrop(e.target.checked)
              touch()
            }}
          />
          No drop — cannot be dropped or given away, but can still be destroyed
        </label>

        <label className="field-check">
          <input
            type="checkbox"
            checked={isLightSource}
            onChange={(e) => {
              setIsLightSource(e.target.checked)
              touch()
            }}
          />
          Light source — lights a dark room while worn or wielded
        </label>
        {isLightSource && (
          <p className="dim detail">
            Any slot counts, so a helm or a pendant works as well as a lantern in a hand. Carrying
            it in the pack does not: a light you have not taken out is not lit. One lit item lights
            the room for everyone standing in it.
          </p>
        )}

        <Field
          label="Food value"
          hint="How much hunger eating this answers. Leave blank if it is not food."
        >
          <input
            type="number"
            min="1"
            value={foodValue}
            onChange={(e) => {
              setFoodValue(e.target.value)
              touch()
            }}
          />
        </Field>

        <Field
          label="Drink value"
          hint="How much thirst drinking this answers. Leave blank if it is not a drink."
        >
          <input
            type="number"
            min="1"
            value={drinkValue}
            onChange={(e) => {
              setDrinkValue(e.target.value)
              touch()
            }}
          />
        </Field>

        {(foodValue.trim() !== '' || drinkValue.trim() !== '') && (
          <p className="dim detail">
            Eating or drinking consumes the item. Hunger and thirst run 0 to 100, so a value of 30
            is about a third of a full belly. A thing can be both — a stew, an ale.
          </p>
        )}

        <Field
          label="Paths"
          hint="None ticked means anyone may use it. Ticking any restricts it to those."
        >
          <div className="check-row">
            {CHARACTER_PATHS.map((path) => (
              <label className="field-check" key={path}>
                <input
                  type="checkbox"
                  checked={paths.includes(path)}
                  onChange={(e) => {
                    // Rebuilt from CHARACTER_PATHS rather than pushed onto, so the saved order is
                    // the enum's however they were ticked - two items restricted to the same two
                    // Paths should not differ by the order somebody clicked.
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

        {paths.length > 0 && slots.length === 0 && (
          /* A Path list on something with no slot restricts nothing: the check runs when an item
             is worn or wielded, and this one never is. Said rather than refused, because an
             authored slot may be on its way. */
          <p className="dim detail">
            This item has no slot, so it is never worn or wielded and the Path list will never be
            consulted.
          </p>
        )}
      </fieldset>

      {STAT_GROUPS.map((group) => (
        <fieldset className="subpanel" key={group.label}>
          <legend>{group.label}</legend>
          {group.hint && <p className="dim detail">{group.hint}</p>}

          <div className="stat-grid">
            {group.fields.map((field) => (
              <Field key={field.key} label={field.label} hint={field.hint}>
                <NumberField
                  value={asNumber(baseStats[field.key])}
                  integer={field.kind === 'int'}
                  onChange={(next) => {
                    setBaseStats((prev) => {
                      const copy = { ...prev }
                      // Blank removes the key rather than writing 0. Zero armour and *no*
                      // armour resolve the same today, but a stored 0 is a claim the item
                      // makes, and a later rule that treats them differently would be wrong.
                      if (next === undefined) delete copy[field.key]
                      else copy[field.key] = next
                      return copy
                    })
                    touch()
                  }}
                />
              </Field>
            ))}
          </div>
        </fieldset>
      ))}

      <fieldset className="subpanel">
        <legend>Other stats</legend>
        {carriedStats.length === 0 && (
          <p className="dim detail">Nothing beyond the fields above.</p>
        )}

        {carriedStats.length > 0 && (
          /* Shown, not editable. These survive a save either way, but a builder who cannot see
             them has no way to know an item has a damage die at all - and invisible content is
             how the coercion bug went unnoticed. */
          <p className="dim detail">
            Also on this item, carried through unchanged:{' '}
            {carriedStats.map(([key, value]) => `${key} = ${String(value)}`).join(', ')}
          </p>
        )}
      </fieldset>

      <div className="row">
        <Button variant="primary" disabled={!dirty || busy} onClick={() => void save()}>
          {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
        </Button>
      </div>

      <ConfirmDialog
        open={deleting}
        onOpenChange={setDeleting}
        title={`Delete ${templateKey}?`}
        description="This cannot be undone."
        destructive
        busy={busy}
        confirmLabel="Delete template"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}

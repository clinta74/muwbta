import type { ItemTemplate } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { NumberField } from '../../ui/NumberField'
import { TemplatePicker } from '../templates/TemplatePicker'
import { clampChance, type LootRow } from './mobStats'

interface Props {
  rows: LootRow[]
  itemTemplates: ItemTemplate[]
  onChange: (rows: LootRow[]) => void
}

/**
 * What a mob drops when it dies.
 *
 * The table was passed through a save untouched and had no editor at all, so a loot table could
 * only be written by seeding or by SQL — which meant the whole loot half of combat was
 * unauthorable, in the same way shopkeepers were before their behavior editor existed.
 *
 * Each row is rolled independently on death, so three rows at 0.5 is not "one of three": it is
 * three separate coin flips, and all three can land.
 */
export function LootEditor({ rows, itemTemplates, onChange }: Props) {
  const options = itemTemplates.map((t) => ({ key: t.key, name: t.name }))

  const edit = (index: number, patch: Partial<LootRow>) =>
    onChange(rows.map((row, i) => (i === index ? { ...row, ...patch } : row)))

  return (
    <fieldset className="subpanel">
      <legend>Loot</legend>
      <p className="dim detail">
        Rolled once per row when this mob dies, each independently — three rows at 0.5 can all
        drop. Chance is 0 to 1, so 0.25 is a quarter of the time.
      </p>

      {rows.length === 0 && <p className="dim">Drops nothing.</p>}

      {rows.map((row, index) => {
        const known = options.some((o) => o.key === row.itemTemplateKey)

        return (
          <div className="field-row" key={index}>
            <Field
              label="Item"
              width="lg"
              /* A key that matches no template is allowed - content is routinely wired before
                 the thing it points at exists (§7.4) - but it is worth saying out loud, because
                 the other reason for it is a typo. */
              error={row.itemTemplateKey && !known ? 'No item template with that key yet.' : null}
            >
              <TemplatePicker
                value={row.itemTemplateKey}
                options={options}
                onChange={(key) => edit(index, { itemTemplateKey: key })}
              />
            </Field>

            <Field label="Chance" width="xs" hint="0.25 = a quarter of kills.">
              <NumberField
                value={row.chance}
                onChange={(next) => edit(index, { chance: clampChance(next ?? 0) })}
              />
            </Field>

            <Button
              variant="danger"
              onClick={() => onChange(rows.filter((_, i) => i !== index))}
            >
              Remove
            </Button>
          </div>
        )
      })}

      <button
        type="button"
        onClick={() => onChange([...rows, { itemTemplateKey: '', chance: 0.25 }])}
      >
        Add drop
      </button>
    </fieldset>
  )
}

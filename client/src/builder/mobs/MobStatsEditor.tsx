import { Field } from '../../ui/Field'
import { NumberField } from '../../ui/NumberField'
import { asNumber } from '../items/stats'
import { MOB_STAT_GROUPS, OWNED_MOB_STAT_KEYS } from './mobStats'

interface Props {
  stats: Record<string, unknown>
  onChange: (next: Record<string, unknown>) => void
}

/**
 * The numbers a mob fights with.
 *
 * The editor offered only health, so a mob's damage, accuracy, and armour were settable by
 * seeding or by SQL and nothing else — the same gap the item editor had, and with the same
 * consequence: a builder could make a mob but not make it dangerous.
 */
export function MobStatsEditor({ stats, onChange }: Props) {
  function set(key: string, value: number | undefined) {
    const next = { ...stats }
    // Blank removes the key. That matters here more than on items: several of these fall back
    // to a level-derived default when absent, and a stored 0 is a declaration of zero rather
    // than a request for the default.
    if (value === undefined) delete next[key]
    else next[key] = value
    onChange(next)
  }

  const carried = Object.entries(stats)
    .filter(([key]) => !OWNED_MOB_STAT_KEYS.includes(key))
    .sort(([a], [b]) => a.localeCompare(b))

  return (
    <>
      {MOB_STAT_GROUPS.map((group) => (
        <fieldset className="subpanel" key={group.label}>
          <legend>{group.label}</legend>
          {group.hint && <p className="dim detail">{group.hint}</p>}

          <div className="stat-grid">
            {group.fields.map((field) => (
              <Field key={field.key} label={field.label} hint={field.hint}>
                <NumberField
                  value={asNumber(stats[field.key])}
                  integer={field.kind === 'int'}
                  onChange={(next) => set(field.key, next)}
                />
              </Field>
            ))}
          </div>
        </fieldset>
      ))}

      {carried.length > 0 && (
        /* Shown, not editable. A builder who cannot see these has no way to know a mob carries
           a damage range string at all - and invisible content is how the item editor's
           coercion bug went unnoticed for so long. */
        <p className="dim detail">
          Also on this mob, carried through unchanged:{' '}
          {carried.map(([key, value]) => `${key} = ${String(value)}`).join(', ')}
        </p>
      )}
    </>
  )
}

import { useEffect, useRef, useState } from 'react'
import { MULTIPLIER_KEYS, type Multipliers } from '../../net/builderApi'
import { Field } from '../../ui/Field'

/** What each factor does, in the terms a builder tuning a zone would think in. */
const HINTS: Record<keyof Multipliers, string> = {
  strength: 'Master dial — scales health and damage together.',
  health: 'Fine-tune health on top of Strength.',
  damage: 'Fine-tune damage on top of Strength.',
  xp: 'XP awarded on kill. 0 makes a zone worth no experience.',
  gold: 'Coin dropped on loot.',
  itemValue: 'What items are worth to a vendor.',
}

const LABELS: Record<keyof Multipliers, string> = {
  strength: 'Strength',
  health: 'Health',
  damage: 'Damage',
  xp: 'XP',
  gold: 'Gold',
  itemValue: 'Item value',
}

interface Props {
  value: Multipliers
  onChange: (next: Multipliers) => void
  /** Shown above the fields — worlds and zones compose, so the wording differs. */
  scope: 'world' | 'zone'
  disabled?: boolean
}

/**
 * The §4.4 difficulty dial (PLAN.md §7.5).
 *
 * Until now these were seed-and-SQL only: `UpsertWorld` / `UpsertZone` carried no multipliers
 * and `WorldWriter` never wrote them, so the one dial the scaling design is built around could
 * not be turned from the browser at all.
 */
export function MultiplierEditor({ value, onChange, scope, disabled }: Props) {
  const set = (key: keyof Multipliers, next: number) => onChange({ ...value, [key]: next })

  return (
    <fieldset className="subpanel">
      <legend>Difficulty multipliers</legend>
      <p className="dim detail">
        1.0 = no change, 2.0 = double, 0.5 = half.{' '}
        {scope === 'zone'
          ? 'Multiplied by the world’s, not replacing it — a 2× zone in a 2× world is 4×.'
          : 'Applies to every zone in this world, multiplied by each zone’s own.'}{' '}
        Resolved once at spawn time, so existing mobs keep the numbers they spawned with.
      </p>

      <div className="multiplier-grid">
        {MULTIPLIER_KEYS.map((key) => (
          <Field key={key} label={LABELS[key]} hint={HINTS[key]}>
            <FactorInput
              value={value[key]}
              disabled={disabled}
              onChange={(next) => set(key, next)}
            />
          </Field>
        ))}
      </div>
    </fieldset>
  )
}

/**
 * A decimal field bound to a number.
 *
 * Keeps its own text buffer while focused, because binding the input straight to the number
 * makes "1." unreachable — the keystroke parses to 1 and React rewrites the field under the
 * cursor before the "5" can be typed. On blur the buffer resnaps to the committed value, so an
 * abandoned partial entry like "0." cannot be left on screen looking saved.
 */
function FactorInput({
  value,
  onChange,
  disabled,
}: {
  value: number
  onChange: (value: number) => void
  disabled?: boolean
}) {
  const [text, setText] = useState(() => String(value))
  const focused = useRef(false)

  useEffect(() => {
    if (!focused.current) setText(String(value))
  }, [value])

  return (
    <input
      type="text"
      inputMode="decimal"
      className="number-input"
      value={text}
      disabled={disabled}
      spellCheck={false}
      onFocus={() => {
        focused.current = true
      }}
      onBlur={() => {
        focused.current = false
        setText(String(value))
      }}
      onChange={(e) => {
        // Digits and a single dot. A multiplier is never negative: a -1 dial has no meaning
        // and Resolve would floor it away, so the minus key is simply not accepted.
        let out = ''
        let dot = false
        for (const c of e.target.value) {
          if (c >= '0' && c <= '9') out += c
          else if (c === '.' && !dot) {
            out += c
            dot = true
          }
        }

        setText(out)

        const parsed = Number(out)
        if (out !== '' && out !== '.' && Number.isFinite(parsed)) onChange(parsed)
      }}
    />
  )
}

import { useEffect, useState } from 'react'
import {
  builderApi,
  type Ability,
  type AbilityEffectSpec,
  type CostType,
  type TargetingType,
} from '../../net/builderApi'
import { ABILITY_EFFECTS, effectOption, pruneParams } from '../effects'
import { ParamInput } from '../ParamInput'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { NumberInput, OptionalNumberInput } from '../../ui/NumberInput'
import { SecondsInput } from '../../ui/SecondsInput'
import { Select } from '../../ui/Select'
import { Textarea } from '../../ui/Textarea'
import { useToast } from '../../ui/Toast'

interface Props {
  abilityKey: string
  /** Every ability in the database, for naming the others on a shared timer. */
  roster: Ability[]
  onChanged: () => void
  onDeleted: () => void
}

const COST_TYPES: CostType[] = ['Stamina', 'Focus', 'Health']
const TARGETING: Array<{ value: TargetingType; label: string }> = [
  { value: 'SingleTarget', label: 'One target' },
  { value: 'Self', label: 'The caster' },
  { value: 'Aoe', label: 'Everyone in the room' },
]

/** One swing. Cooldowns that are not a multiple of this drift against the fight (PLAN.md §2.3). */
const PULSES_PER_BEAT = 8

export function AbilityEditor({ abilityKey, roster, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const [ability, setAbility] = useState<Ability | null>(null)
  const [draft, setDraft] = useState<Ability | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)

  useEffect(() => {
    let cancelled = false
    void builderApi
      .ability(abilityKey)
      .then((loaded) => {
        if (cancelled) return
        setAbility(loaded)
        setDraft(loaded)
      })
      .catch(() => setError('Could not load this ability.'))
    return () => {
      cancelled = true
    }
  }, [abilityKey])

  if (!draft || !ability) {
    return <p className="dim">{error ?? 'Loading…'}</p>
  }

  const set = (patch: Partial<Ability>) => setDraft({ ...draft, ...patch })
  const dirty = JSON.stringify(draft) !== JSON.stringify(ability)

  const setEffect = (index: number, next: AbilityEffectSpec) =>
    set({ effects: draft.effects.map((e, i) => (i === index ? next : e)) })

  const beats = draft.cooldownPulses / PULSES_PER_BEAT
  const onBeat = draft.cooldownPulses % PULSES_PER_BEAT === 0

  // The others on this timer. Matched on Path as well as on the number, because the number is only
  // half a timer's identity: a character only ever knows one Path's abilities, so Warden 1 and
  // Temper 1 can never meet in play and are two timers rather than one.
  const onTimer =
    draft.cooldownGroup === null
      ? []
      : roster.filter(
          (a) =>
            a.key !== draft.key &&
            a.path === draft.path &&
            a.cooldownGroup === draft.cooldownGroup,
        )

  async function save() {
    if (!draft) return
    setBusy(true)
    setError(null)
    try {
      const saved = await builderApi.updateAbility(draft.key, {
        path: draft.path,
        unlockLevel: draft.unlockLevel,
        name: draft.name,
        description: draft.description,
        costType: draft.costType,
        costValue: draft.costValue,
        cooldownPulses: draft.cooldownPulses,
        cooldownGroup: draft.cooldownGroup,
        castTimePulses: draft.castTimePulses,
        targetingType: draft.targetingType,
        effects: draft.effects.map((e) => ({
          key: e.key,
          params: pruneParams(e.key, e.params) ?? {},
        })),
      })
      setAbility(saved)
      setDraft(saved)
      toast.notify(`Saved ${saved.name}.`)
      onChanged()
    } catch (e) {
      // The server refuses anything that would not work, and its message names the reason. That
      // is the whole value of the refusal, so it is shown verbatim rather than replaced with
      // "could not save".
      setError(e instanceof Error ? e.message : 'Could not save this ability.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="template-editor">
      <div className="room-editor-head">
        <h2>{ability.name}</h2>
        <code className="dim">{ability.key}</code>
      </div>

      {error && <p className="bad">{error}</p>}

      {ability.problems.length > 0 && (
        <ul className="ability-problems">
          {ability.problems.map((p, i) => (
            <li key={i} className={p.severity === 'Error' ? 'bad' : 'warn'}>
              {p.message}
            </li>
          ))}
        </ul>
      )}

      <div className="field-row">
        <Field label="Name" width="lg">
          <input value={draft.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field
          label="Path"
          width="md"
          hint="Changing this needs the key to match — rename the ability instead."
        >
          <Select value={draft.path} onChange={() => undefined} disabled>
            <option value={draft.path}>{draft.path}</option>
          </Select>
        </Field>
        <Field label="Unlocks at" width="xs" hint="Character level.">
          <NumberInput
            min={1}
            max={50}
            value={draft.unlockLevel}
            onChange={(v) => set({ unlockLevel: v })}
          />
        </Field>
      </div>

      <Field label="Description" hint="Read by the player on the abilities screen.">
        <Textarea
          value={draft.description}
          onChange={(v) => set({ description: v })}
          rows={3}
        />
      </Field>

      <p className="dim detail">
        Focus for spells, Stamina for skills — the cost type is what makes it one or the other
        (§4.7).
      </p>

      <div className="field-row">
        <Field label="Costs" width="sm">
          <Select value={draft.costType} onChange={(v) => set({ costType: v as CostType })}>
            {COST_TYPES.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Amount" width="xs">
          <NumberInput min={1} value={draft.costValue} onChange={(v) => set({ costValue: v })} />
        </Field>
        <Field label="Targets" width="md">
          <Select
            value={draft.targetingType}
            onChange={(v) => set({ targetingType: v as TargetingType })}
          >
            {TARGETING.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </Select>
        </Field>
      </div>

      <div className="field-row">
        <Field
          label="Cooldown"
          width="sm"
          hint={
            onBeat
              ? `Seconds — ${beats} swing${beats === 1 ? '' : 's'} of the 2s combat beat.`
              : 'Seconds. Not a whole number of 2s swings, so it drifts against the fight.'
          }
        >
          <SecondsInput
            pulses={draft.cooldownPulses}
            onChange={(v) => set({ cooldownPulses: v })}
          />
        </Field>
        <Field label="Cast time" width="sm" hint="Seconds. Zero is instant, and a cast can be interrupted.">
          <SecondsInput
            pulses={draft.castTimePulses ?? 0}
            onChange={(v) => set({ castTimePulses: v === 0 ? null : v })}
          />
        </Field>
        <Field
          label="Shared timer"
          width="sm"
          hint="Blank shares nothing. Numbered per Path."
        >
          <OptionalNumberInput
            min={1}
            value={draft.cooldownGroup}
            onChange={(v) => set({ cooldownGroup: v })}
            aria-label="Shared timer"
          />
        </Field>
      </div>

      {draft.cooldownGroup !== null && (
        <p className={onTimer.length === 0 ? 'warn' : 'dim'}>
          {onTimer.length === 0 ? (
            <>
              Nothing else is on {draft.path} timer {draft.cooldownGroup}, so this shares with
              nothing. Put another ability on the same number to make it mean something.
            </>
          ) : (
            <>
              On {draft.path} timer {draft.cooldownGroup}: {onTimer.map((a) => a.name).join(' · ')}.
              Using any one of these puts the whole timer on that ability&rsquo;s cooldown.
            </>
          )}
        </p>
      )}

      <fieldset className="subpanel">
        <legend>Effects</legend>

        <p className="dim">
          Applied in order, and all of them land — this is a list of what the ability does, not a
          choice between them. The cost and the cooldown are still charged once.
        </p>

        {draft.effects.map((effect, index) => {
          const option = effectOption(effect.key)

          return (
            <div className="attack-card" key={index}>
              <div className="attack-card-head">
                <Field label="Does" hint={option?.summary}>
                  <Select
                    value={effect.key}
                    onChange={(v) =>
                      // The old effect's parameters go rather than carrying across. Executors skip
                      // keys they do not recognise, so a leftover tickDamage on a heal is
                      // invisible rather than wrong — until somebody reads the row and cannot tell
                      // what it was meant to do.
                      setEffect(index, { key: v, params: {} })
                    }
                  >
                    {ABILITY_EFFECTS.map((e) => (
                      <option key={e.key} value={e.key}>
                        {e.label}
                      </option>
                    ))}
                  </Select>
                </Field>

                {/* An ability with no effects is refused by the server, so the last one cannot be
                    removed here — a control that produces a save the server will reject is worse
                    than one that is not offered. */}
                {draft.effects.length > 1 && (
                  <Button
                   
                    variant="danger"
                    onClick={() => set({ effects: draft.effects.filter((_, i) => i !== index) })}
                  >
                    Remove
                  </Button>
                )}
              </div>

              {option ? (
                <div className="stat-grid">
                  {option.params.map((param) => (
                    <Field key={param.key} label={param.label} hint={param.hint}>
                      <ParamInput
                        param={param}
                        stored={effect.params[param.key] ?? ''}
                        onChange={(stored) =>
                          setEffect(index, {
                            key: effect.key,
                            params: { ...effect.params, [param.key]: stored },
                          })
                        }
                      />
                    </Field>
                  ))}
                </div>
              ) : (
                <p className="bad">
                  Names <code>{effect.key}</code>, which no executor implements. The whole ability
                  fizzles — one missing executor stops the rest of the list running too.
                </p>
              )}
            </div>
          )
        })}

        <button
          type="button"
          onClick={() =>
            set({
              effects: [
                ...draft.effects,
                // Seeded with the fallbacks, so a freshly added effect is valid rather than being
                // refused for a blank the builder has not reached yet.
                {
                  key: ABILITY_EFFECTS[0].key,
                  params: Object.fromEntries(
                    ABILITY_EFFECTS[0].params.map((p) => [p.key, p.fallback]),
                  ),
                },
              ],
            })
          }
        >
          Add effect
        </button>
      </fieldset>

      <div className="row">
        <Button variant="primary" disabled={!dirty || busy} onClick={() => void save()}>
          {busy ? 'Saving…' : 'Save'}
        </Button>
        <button type="button" disabled={!dirty || busy} onClick={() => setDraft(ability)}>
          Revert
        </button>
        <Button variant="danger" onClick={() => setConfirming(true)}>
          Delete
        </Button>
      </div>

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={`Delete ${ability.name}?`}
        confirmLabel="Delete"
        destructive
        description="Anyone whose Path and level granted it stops knowing it at once. Characters keep their levels; there is simply nothing at this one until something replaces it."
        onConfirm={async () => {
          await builderApi.deleteAbility(ability.key)
          onDeleted()
        }}
      />
    </div>
  )
}

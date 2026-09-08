import { useState } from 'react'
import type { FlagValue } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { FlagControl, flagIsPositive, flagLabel } from './FlagControl'

interface Props {
  scope: 'world' | 'zone'
  /** The flags this entity declares itself. A key that is absent is inherited, not off. */
  flags: Record<string, FlagValue>
  inheritedNote: string
  onSet: (key: string, value: FlagValue | null) => Promise<unknown>
}

/**
 * Three-state flags for a world or a zone, rendered from the server registry.
 *
 * The same shape as `RoomFlagsTab`, and for the same reason: "off" is a decision, while
 * "inherit" removes the key and lets the level above decide. The zone panel used to hardcode
 * `pvp` and `peaceful` as two-state buttons, so it could express neither the distinction nor any
 * flag added later.
 *
 * Each toggle is a single-flag PUT, like the room editor's. It was briefly a whole-map write, and
 * two builders in one zone would then silently overwrite each other's flags — the loser's edit
 * vanished with no error, since the winning request carried a perfectly valid map.
 */
export function ScopedFlagList({ scope, flags, inheritedNote, onSet }: Props) {
  const { flagDefinitions } = useBuilderData()
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState<string | null>(null)

  function set(key: string, value: FlagValue | null) {
    setError(null)
    setPending(key)
    void onSet(key, value)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not set that flag.'))
      .finally(() => setPending(null))
  }

  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}
      <p className="dim detail">{inheritedNote}</p>

      <ul className="flag-list">
        {flagDefinitions.map((definition) => {
          const own = flags[definition.key]
          const declared = own !== undefined

          return (
            <li key={definition.key} className={declared ? 'flag' : 'flag inherited'}>
              <div className="flag-head">
                <strong>{definition.key}</strong>
                {declared ? (
                  <span className={flagIsPositive(own) ? 'good' : 'dim'}>{flagLabel(own)}</span>
                ) : (
                  <span className="dim">inherited</span>
                )}
              </div>

              <p className="dim detail">
                {definition.summary}
                {/*
                  The phase is what tells a builder this flag is not wired up yet. It has been on
                  the wire since the registry existed and was rendered by neither consumer, which
                  is why `indoors` — 30 authored rooms, zero readers — looked exactly like `pvp`
                  (BUGS.md #19). The field meant to prevent that defect was an instance of it.
                */}
                {definition.phase === 'later' && (
                  <span className="flag-later"> · not implemented yet</span>
                )}
              </p>

              <FlagControl
                definition={definition}
                own={own}
                disabled={pending === definition.key}
                inheritTitle={
                  scope === 'zone'
                    ? 'Remove the key so the world decides'
                    : 'Remove the key so the registry default decides'
                }
                onSet={(next) => set(definition.key, next)}
              />
            </li>
          )
        })}
      </ul>
    </div>
  )
}

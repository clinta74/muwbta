import { useState } from 'react'
import { builderApi, type FlagValue, type RoomDetail } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { FlagControl, flagIsPositive, flagLabel } from '../world/FlagControl'

interface Props {
  room: RoomDetail
  onChanged: (room: RoomDetail) => void
}

/**
 * The three-state flag control. "off" is a decision about this room; "inherit" removes the key
 * so the zone or world decides - a two-state checkbox could not tell those apart.
 *
 * Each toggle is a single-flag PUT, not a whole-map write, so two builders in one zone stop
 * erasing each other (PLAN §1). Flags render from the server registry, so a newly registered
 * flag appears here with no client change.
 */
export function RoomFlagsTab({ room, onChanged }: Props) {
  const { flagDefinitions } = useBuilderData()
  const [error, setError] = useState<string | null>(null)

  function set(key: string, value: FlagValue | null) {
    void builderApi
      .setRoomFlag(room.key, key, value)
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not set that flag.'))
  }

  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}

      <ul className="flag-list">
        {flagDefinitions.map((definition) => {
          const resolved = room.resolved.find((r) => r.key === definition.key)
          const own = room.flags[definition.key]
          const declared = own !== undefined
          const value = resolved?.value ?? definition.default

          return (
            <li key={definition.key} className={declared ? 'flag' : 'flag inherited'}>
              <div className="flag-head">
                <strong>{definition.key}</strong>
                <span className={flagIsPositive(value) ? 'good' : 'dim'}>{flagLabel(value)}</span>
                {!declared && resolved && resolved.source !== 'default' && (
                  <span className="dim"> · from {resolved.source}</span>
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
                inheritTitle="Remove the key so the zone or world decides"
                onSet={(next) => set(definition.key, next)}
              />
            </li>
          )
        })}
      </ul>
    </div>
  )
}

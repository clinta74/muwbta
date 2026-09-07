import { useEffect, useState } from 'react'
import { builderApi, type RoomDetail, type TerrainKindInfo } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { Select } from '../../ui/Select'
import { GridPainter } from '../GridPainter'

interface Props {
  room: RoomDetail
  onChanged: (room: RoomDetail) => void
}

/** The ASCII terrain grid and its legend. Autosaves on every stroke - a field-scoped PATCH. */
export function RoomTerrainTab({ room, onChanged }: Props) {
  const [error, setError] = useState<string | null>(null)
  const [kinds, setKinds] = useState<TerrainKindInfo[]>([])
  const [kind, setKind] = useState('')
  const [drawing, setDrawing] = useState(false)

  useEffect(() => {
    let live = true
    void builderApi
      .terrainKinds()
      .then((loaded) => {
        if (!live) return
        setKinds(loaded)
        setKind((current) => current || loaded[0]?.key || '')
      })
      // A server too old to know about terrain kinds leaves the painter exactly as it was, which
      // is the same rule the assist follows: the builder works without the extra.
      .catch(() => undefined)
    return () => {
      live = false
    }
  }, [])

  const save = (grid: string[], legend: Record<string, string>) => {
    void builderApi
      .updateRoom(room.key, { grid, legend })
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not save terrain.'))
  }

  const generate = () => {
    setDrawing(true)
    setError(null)

    void builderApi
      .roomTerrain(room.key, kind)
      .then((terrain) => save(terrain.grid, terrain.legend))
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not draw terrain.'))
      .finally(() => setDrawing(false))
  }

  return (
    <div className="section-body">
      <p className="dim detail">Changes save as you paint.</p>
      {error && <p className="bad">{error}</p>}

      {kinds.length > 0 && (
        <div className="row">
          <Select value={kind} onChange={setKind} aria-label="Terrain kind">
            {kinds.map((k) => (
              <option key={k.key} value={k.key}>
                {k.key} — {k.summary}
              </option>
            ))}
          </Select>

          <Button disabled={drawing || !kind} onClick={generate}>
            {drawing ? 'Drawing…' : 'Generate'}
          </Button>

          {/*
            Said plainly because it is the one surprising thing about this button. The map is drawn
            from the room key rather than from chance, so pressing it again gives exactly the same
            map - which is what stops a regenerated zone from rewriting every room in the diff
            (WORLD.md §10.1). Somebody expecting a reroll should know it is not one.
          */}
          <span className="dim detail">
            Replaces the map below. The same room always draws the same map — paint over it
            afterwards for anything set-piece.
          </span>
        </div>
      )}

      <GridPainter grid={room.grid} legend={room.legend} onChange={save} />
    </div>
  )
}

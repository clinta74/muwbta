import { useEffect, useRef, useState } from 'react'
import { BuilderColumns } from '../BuilderColumns'
import { useNavigate, useOutletContext, useParams } from 'react-router'
import { useCompactBuilder } from '../../ui/pointer'
import { useBuilderData } from '../BuilderData'
import { useNavGuard } from '../NavGuard'
import { keysFromParams, toWorldPath, type Section } from '../routes'
import type { BuilderOutletContext } from '../BuilderShell'
import { ZoneCanvas } from '../ZoneCanvas'
import { RoomEditor } from '../room/RoomEditor'
import { WorldTree } from './WorldTree'
import { WorldPanel } from './WorldPanel'
import { ZonePanel } from './ZonePanel'
import { ValidationPanel } from './ValidationPanel'

/**
 * The world tab: tree on the left, canvas + room editor in the middle, zone flags + warnings
 * on the right. Selection is entirely URL-driven; this component turns route params into data
 * loads and clicks into navigations.
 */
export function WorldTab() {
  const navigate = useNavigate()
  const params = useParams()
  const { worldKey, zoneKey, roomKey, section } = keysFromParams(params)
  const { worlds, rooms, validation, loadZones, loadZone } = useBuilderData()
  const { occupiedRoom } = useOutletContext<BuilderOutletContext>()
  const guard = useNavGuard()
  const compact = useCompactBuilder()
  const [mapOpen, setMapOpen] = useState(false)

  // No world in the URL yet: land on the first one, replacing so Back does not trap here.
  useEffect(() => {
    if (!worldKey && worlds.length > 0) {
      navigate(toWorldPath(worlds[0].key), { replace: true })
    }
  }, [worldKey, worlds, navigate])

  useEffect(() => {
    void loadZones(worldKey)
  }, [worldKey, loadZones])

  useEffect(() => {
    void loadZone(zoneKey)
  }, [zoneKey, loadZone])

  // Walking re-targets the editor. This must fire only on an *actual move* - a ref tracks the last
  // room we followed to, so re-runs caused by the navigate identity changing (which happens on
  // every navigation) do not yank you back off a room you just clicked.
  // Replace, not push, so a walk does not bury the Back button one entry per room crossed.
  //
  // Always on, and it used to be a checkbox. The three things that would have made a switch worth
  // having are already true of the effect: it moves you only when the character moves, it stands
  // off a form with unsaved edits, and the ref means clicking a room keeps you there. What was
  // left was a setting whose off position nobody wanted.
  const followedTo = useRef<string | null>(null)
  useEffect(() => {
    if (!occupiedRoom || guard.isDirty()) return
    if (occupiedRoom === followedTo.current) return
    followedTo.current = occupiedRoom
    const [w, z] = occupiedRoom.split('.')
    navigate(toWorldPath(w, `${w}.${z}`, occupiedRoom, section), { replace: true })
  }, [occupiedRoom, section, navigate, guard])

  // Room and world/zone changes route through the guard so unsaved prose prompts first.
  const goRoom = (key: string) => guard.run(() => navigate(toWorldPath(worldKey, zoneKey, key)))
  const goWorld = (key: string) => guard.run(() => navigate(toWorldPath(key)))
  const goZone = (key: string) => guard.run(() => navigate(toWorldPath(worldKey, key)))
  const roomWarnings = validation?.warnings.filter((w) => w.entityKey === roomKey) ?? []

  return (
    <BuilderColumns
      left={
        <aside className="builder-col">
                <WorldTree
                  worldKey={worldKey}
                  zoneKey={zoneKey}
                  roomKey={roomKey}
                  onWorld={goWorld}
                  onZone={goZone}
                  onRoom={goRoom}
                />
              </aside>
      }
      main={
        <main className="builder-col">
                {/*
                  On a narrow screen the canvas is summoned rather than resident (MOBILE.md §5). It is the
                  one part of the builder that genuinely needs a large pointer-driven surface, and sharing
                  a 390px screen with the room editor would leave neither usable. Everything else here is
                  a form, and forms are the one thing a phone has always been good at.
        
                  Unmounted while closed, not hidden: a canvas nobody can see should not be laying out a
                  zone's worth of boxes on every edit.
                */}
                {zoneKey && !compact && (
                  <ZoneCanvas
                    rooms={rooms}
                    selected={roomKey}
                    occupied={occupiedRoom}
                    onSelect={goRoom}
                    onChanged={() => void loadZone(zoneKey)}
                  />
                )}
        
                {zoneKey && compact && (
                  <button type="button" className="map-summon" onClick={() => setMapOpen(true)}>
                    ▦ Zone map
                    <span className="dim"> · {rooms.length} rooms</span>
                  </button>
                )}
        
                {zoneKey && compact && mapOpen && (
                  <div className="canvas-overlay">
                    <div className="canvas-overlay-head">
                      <span className="dim">{zoneKey}</span>
                      <button type="button" onClick={() => setMapOpen(false)}>
                        ✕ Close map
                      </button>
                    </div>
        
                    <ZoneCanvas
                      rooms={rooms}
                      selected={roomKey}
                      occupied={occupiedRoom}
                      onSelect={(key) => {
                        goRoom(key)
        
                        // Choosing a room is what the map was opened for, and the editor it opens is
                        // underneath. Leaving the map up would hide the answer to the tap.
                        setMapOpen(false)
                      }}
                      onChanged={() => void loadZone(zoneKey)}
                    />
                  </div>
                )}
        
                {roomKey ? (
                  <RoomEditor
                    key={roomKey}
                    roomKey={roomKey}
                    section={section}
                    warnings={roomWarnings}
                    onSection={(s: Section) => navigate(toWorldPath(worldKey, zoneKey, roomKey, s))}
                    onChanged={() => void loadZone(zoneKey)}
                    onDeleted={() => navigate(toWorldPath(worldKey, zoneKey))}
                    onNavigate={goRoom}
                  />
                ) : (
                  <p className="dim">Pick a room, or dig one from the room you are standing in.</p>
                )}
              </main>
      }
      right={
        <aside className="builder-col">
                {/* The zone panel wins the slot when a zone is selected: it is the narrower scope, and
                    it carries the difficulty preview, which is what a builder is usually here for. The
                    world panel is how you reach a world's own properties at all. */}
                {zoneKey ? (
                  <ZonePanel zoneKey={zoneKey} />
                ) : (
                  worldKey && (
                    <WorldPanel worldKey={worldKey} onDeleted={() => navigate(toWorldPath(null))} />
                  )
                )}
                <ValidationPanel validation={validation} onSelect={goRoom} />
              </aside>
      }
    />
  )
}

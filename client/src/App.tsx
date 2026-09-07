import { useCallback, useEffect, useRef, useState } from 'react'
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router'
import { AuthScreen, CharacterScreen } from './components/AuthScreen'
import { GameScreen } from './components/GameScreen'
import { MapShell } from './components/MapShell'
import { BuilderShell } from './builder/BuilderShell'
import { WorldTab } from './builder/world/WorldTab'
import { AbilitiesTab } from './builder/abilities/AbilitiesTab'
import { MobsTab } from './builder/mobs/MobsTab'
import { ItemsTab } from './builder/items/ItemsTab'
import { QuestsTab } from './builder/quests/QuestsTab'
import { AccountsTab } from './builder/accounts/AccountsTab'
import { SetupTab } from './builder/setup/SetupTab'
import { api, type Account, type Character } from './net/api'
import './game/game.scss'

type Stage =
  | { name: 'loading' }
  | { name: 'anonymous' }
  // `departed` carries the character that was just walked out of the world, so the list can show
  // it as gone before the server has finished being told. It lives on the stage rather than in its
  // own state because every route into 'choosing' then has to say what it means - there is nothing
  // left over from the previous visit to forget to clear.
  | { name: 'choosing'; account: Account; departed?: string }
  | { name: 'playing'; account: Account; character: Character }

const BUILDER_ROLES = ['Builder', 'Admin']

export default function App() {
  const [stage, setStage] = useState<Stage>({ name: 'loading' })

  // Why the player is back at the character list when it was not their idea. Only a takeover by
  // another device sets it, and choosing a character clears it.
  const [notice, setNotice] = useState<string | null>(null)
  const [currentRoom, setCurrentRoom] = useState<string | null>(null)
  const focusInputRef = useRef<(() => void) | null>(null)
  const navigate = useNavigate()
  const location = useLocation()
  const inBuilder = location.pathname.startsWith('/builder')
  const inMap = location.pathname.startsWith('/map')

  // Anything drawn over the game hides the workspace and stops it reading keystrokes. Both are
  // the same question - "is the game the thing on screen" - so they are one flag rather than a
  // condition each caller has to remember to widen when a third overlay arrives.
  const overlaid = inBuilder || inMap

  // The session cookie survives a reload, so check for an existing login before showing
  // the form - otherwise a refresh mid-session looks like being logged out.
  useEffect(() => {
    void api
      .me()
      .then((account) => setStage({ name: 'choosing', account }))
      .catch(() => setStage({ name: 'anonymous' }))
  }, [])

  // Stable so GameScreen's effect does not re-fire on every render of App.
  const onRoomChange = useCallback((roomKey: string) => setCurrentRoom(roomKey), [])

  // Focus the game input when returning from whatever was over it.
  useEffect(() => {
    if (!overlaid) {
      focusInputRef.current?.()
    }
  }, [overlaid])

  async function logout() {
    await api.logout().catch(() => undefined)
    navigate('/')
    setStage({ name: 'anonymous' })
  }

  switch (stage.name) {
    case 'loading':
      return (
        <main className="shell">
          <p className="dim">Connecting…</p>
        </main>
      )

    case 'anonymous':
      return <AuthScreen onReady={(account) => setStage({ name: 'choosing', account })} />

    case 'choosing':
      return (
        <CharacterScreen
          notice={notice}
          departed={stage.departed}
          onEnter={(character) => {
            setNotice(null)
            setStage({ name: 'playing', account: stage.account, character })
          }}
          onLogout={() => void logout()}
        />
      )

    case 'playing': {
      const canBuild = BUILDER_ROLES.includes(stage.account.role)
      const isAdmin = stage.account.role === 'Admin'

      return (
        <>
          {/* Hidden rather than unmounted while the builder is open. Unmounting would close
              the SSE stream, mark the character link-dead, and reconnect on the way back -
              and follow mode depends on that stream staying live to know where you are.
              GameScreen sits OUTSIDE <Routes> for the same reason: route changes must not
              remount it. */}
          <div className={overlaid ? 'workspace hidden' : 'workspace'}>
            <GameScreen
              characterId={stage.character.id}
              characterName={stage.character.name}
              onRoomChange={onRoomChange}
              // A path opens the builder on that exact entity — the deep links `examine` and
              // `stats` hand a builder. No path is the plain "open the builder" button.
              //
              // The typeof guard is deliberate: an optional-parameter callback is assignable to
              // `() => void`, so a caller that wires this straight to onClick type-checks and
              // then passes a MouseEvent as the "path". That is exactly how the builder button
              // broke, and it broke silently.
              onOpenBuilder={
                canBuild
                  ? (path?: string) => navigate(typeof path === 'string' ? path : '/builder')
                  : undefined
              }
              // Open to everyone, unlike the builder - a map is of the world, not a way to
              // change it. Wrapped for the same reason onOpenBuilder is: wiring a handler
              // straight to onClick hands it React's MouseEvent as its first argument.
              onOpenMap={() => navigate('/map')}
              onLeave={() => {
                // Frees the slot against the per-account cap straight away rather than waiting
                // out the 90 s link-dead window.
                void api.leave(stage.character.id).catch(() => undefined)
                setNotice(null)
                navigate('/')
                setStage({
                  name: 'choosing',
                  account: stage.account,
                  departed: stage.character.id,
                })
              }}
              // Another device took this character. Pointedly *not* calling `api.leave`: the
              // character is in the world being played, and leaving would pull it out from under
              // the device that now has it - this screen's own tidiness costing somebody else
              // their session.
              onDisplaced={(message) => {
                setNotice(message)
                navigate('/')
                setStage({ name: 'choosing', account: stage.account })
              }}
              focusInputRef={focusInputRef}
              // Hidden is not unmounted, so the game has to be told to stop listening for
              // keystrokes - otherwise typing a room name in the builder would type it here.
              active={!overlaid}
            />
          </div>

          <Routes>
            {/* Open to any logged-in player, so there is no role check here and none on the
                server either - see MapEndpoints. */}
            <Route
              path="/map"
              element={
                <MapShell
                  currentWorld={currentRoom?.split('.')[0] ?? null}
                  onClose={() => navigate('/')}
                />
              }
            />

            <Route
              path="/builder"
              element={
                canBuild ? (
                  <BuilderShell
                    occupiedRoom={currentRoom}
                    isAdmin={isAdmin}
                    onClose={() => navigate('/')}
                  />
                ) : (
                  <Navigate to="/" replace />
                )
              }
            >
              <Route index element={<Navigate to="world" replace />} />
              <Route path="world/:world?/:zone?/:room?/:section?" element={<WorldTab />} />
              <Route path="mobs/:templateKey?" element={<MobsTab />} />
              <Route path="abilities/:abilityKey?" element={<AbilitiesTab />} />
              <Route path="items/:templateKey?" element={<ItemsTab />} />
              <Route path="quests/:questKey?" element={<QuestsTab />} />
              <Route path="setup/:section?" element={<SetupTab />} />

              {/* Admin-only, and refused here as well as in the tab bar - hiding a tab is not
                  access control, and this route is reachable by typing it. The API refuses a
                  non-admin independently, so this redirect is a courtesy rather than the
                  boundary. */}
              <Route
                path="accounts/:username?"
                element={isAdmin ? <AccountsTab /> : <Navigate to="/builder/world" replace />}
              />
            </Route>

            {/*
              Everything that is not the builder renders nothing *here*, because the game screen
              lives outside <Routes> on purpose — unmounting it would close the SSE stream and mark
              the character link-dead (see the workspace div above).

              So `/` legitimately matches no route, and React Router says so on every render:
              "No routes matched location /". Matching it explicitly with a null element is the
              difference between a router that has been told the root is intentional and one that
              is reporting a gap. It also covers a mistyped path, which shows the game rather than
              a blank screen.
            */}
            <Route path="*" element={null} />
          </Routes>
        </>
      )
    }
  }
}

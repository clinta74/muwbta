import { useEffect, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { useCompactBuilder } from '../components/pointer'
import { BuilderDataProvider } from './BuilderData'
import { NavGuardProvider, useNavGuard } from './NavGuard'
import { ToastProvider } from '../ui/Toast'
import { Tabs, type TabItem } from '../ui/Tabs'
import type { BuilderTab } from './routes'
import './builder.scss'

/** Passed down to the routed tabs via the router Outlet. */
export interface BuilderOutletContext {
  occupiedRoom: string | null
}

interface BuilderShellProps {
  occupiedRoom: string | null
  /** Admins get the Accounts tab; builders do not, and the route refuses them as well. */
  isAdmin: boolean
  onClose: () => void
}

const WORLD_TABS: TabItem[] = [
  { value: 'world', label: 'World' },
  { value: 'mobs', label: 'Mobs' },
  { value: 'items', label: 'Items' },
  { value: 'abilities', label: 'Abilities' },
  { value: 'quests', label: 'Quests' },
  { value: 'setup', label: 'Setup' },
]

const ACCOUNTS_TAB: TabItem = { value: 'accounts', label: 'Accounts' }

function tabFromPath(pathname: string): BuilderTab {
  if (pathname.startsWith('/builder/mobs')) return 'mobs'
  if (pathname.startsWith('/builder/items')) return 'items'
  if (pathname.startsWith('/builder/abilities')) return 'abilities'
  if (pathname.startsWith('/builder/quests')) return 'quests'
  if (pathname.startsWith('/builder/setup')) return 'setup'
  if (pathname.startsWith('/builder/accounts')) return 'accounts'
  return 'world'
}

/**
 * The builder chrome: header, the tab bar (World/Mobs/Items/Quests/Setup, plus Accounts for
 * admins),
 * and a router Outlet the tabs fill.
 * The data provider, toast host, and unsaved-changes guard live here so every tab and dialog
 * shares them.
 */
export function BuilderShell(props: BuilderShellProps) {
  return (
    <BuilderDataProvider>
      <ToastProvider>
        <NavGuardProvider>
          <ShellBody {...props} />
        </NavGuardProvider>
      </ToastProvider>
    </BuilderDataProvider>
  )
}

function ShellBody({ occupiedRoom, isAdmin, onClose }: BuilderShellProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const guard = useNavGuard()
  const tab = tabFromPath(location.pathname)

  // Below ~768px the three panes cannot share the width, so the first one — the tree, whichever
  // tab it belongs to — becomes a drawer over the editor.
  const compact = useCompactBuilder()
  const [navOpen, setNavOpen] = useState(false)

  // Picking something from the tree is the end of what the drawer is for. Closing on every
  // navigation is cruder than watching the selection and exactly as correct here, since the only
  // things in the drawer are links.
  useEffect(() => setNavOpen(false), [location.pathname])

  // Hiding the tab is presentation, not access control - the route and the API both refuse a
  // non-admin independently, so a hand-typed /builder/accounts gets nowhere.
  const tabs = isAdmin ? [...WORLD_TABS, ACCOUNTS_TAB] : WORLD_TABS

  return (
    <div className="builder" data-nav={compact && navOpen ? 'open' : 'closed'}>
      <div className="builder-topbar">
        {compact && (
          <button
            type="button"
            className="builder-nav-toggle"
            onClick={() => setNavOpen((open) => !open)}
            aria-expanded={navOpen}
            aria-label="Show the list"
          >
            ☰
          </button>
        )}

        <h2 className="builder-brand">World builder</h2>

        <Tabs
          value={tab}
          onValueChange={(value) => guard.run(() => navigate(`/builder/${value}`))}
          tabs={tabs}
          aria-label="Builder sections"
        />

        <div className="builder-topbar-right">
          {/* Where the character is standing, shown rather than offered as a setting. Following
              used to be a checkbox and is now simply how the World tab behaves: it only moves you
              when you walk, it will not pull you off a form with unsaved edits, and it will not
              pull you off a room you clicked. With all three of those already true there was
              nothing left for the checkbox to protect you from, and a setting that is correct in
              one position is a question nobody needs asked. */}
          {tab === 'world' && occupiedRoom && (
            <span className="follow-room" title="The builder follows your character as you walk">
              Following <code className="dim">{occupiedRoom}</code>
            </span>
          )}
          <button
            type="button"
            className="builder-exit"
            onClick={() => guard.run(onClose)}
            title="Return to the game"
          >
            ✕ Exit builder
          </button>
        </div>
      </div>

      {/* Tapping beside the open drawer closes it, which is what every drawer does and what a
          thumb reaches for before it finds the ☰ again. */}
      {compact && navOpen && (
        <button
          type="button"
          className="builder-nav-scrim"
          aria-label="Close the list"
          onClick={() => setNavOpen(false)}
        />
      )}

      <Outlet context={{ occupiedRoom } satisfies BuilderOutletContext} />
    </div>
  )
}

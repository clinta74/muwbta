import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { BuilderColumns } from '../BuilderColumns'
import { builderApi, type GameConfigurationList } from '../../net/builderApi'
import { toSetupPath, type SetupSection } from '../routes'
import { ConfigurationsPanel } from './ConfigurationsPanel'
import { ModerationPanel } from './ModerationPanel'
import { TokensPanel } from './TokensPanel'
import { TransferPanel } from './TransferPanel'

const SECTIONS: Array<{ value: SetupSection; label: string; hint: string }> = [
  {
    value: 'configurations',
    label: 'Starter configurations',
    hint: 'Where new characters wake up, and what they are told',
  },
  {
    value: 'moderation',
    label: 'Moderation',
    hint: 'What nobody may say here, for the whole server',
  },
  {
    value: 'transfer',
    label: 'Import & export',
    hint: 'Move authored content between servers',
  },
  {
    value: 'tokens',
    label: 'Access tokens',
    hint: 'Let an agent or a script reach the builder API',
  },
]

/**
 * Server-wide setup (PLAN.md §4.16, §6) — the two things that are about the whole world rather
 * than about one entity in it.
 *
 * Its own tab rather than a corner of the World tab, because neither belongs to a world: a starter
 * configuration names a room in one but is not part of it, and an import can carry several. The
 * other tabs are all "pick a thing, edit the thing"; this one is not, so it does not pretend to be.
 */
export function SetupTab() {
  const navigate = useNavigate()
  const { section } = useParams()
  const current: SetupSection =
    section === 'transfer' || section === 'tokens' || section === 'moderation'
      ? section
      : 'configurations'

  const [list, setList] = useState<GameConfigurationList | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Not cached across visits, and deliberately: which configuration is live can change from
  // another builder's session, and a stale answer to "what does this server do on login" is worse
  // than a spinner.
  const reload = useCallback(() => {
    void builderApi
      .configurations()
      .then((rows) => {
        setList(rows)
        setError(null)
      })
      .catch((e: unknown) => {
        setList({
          configurations: [],
          activeStartingRoomKey: '',
          activeWelcomeMessage: '',
          canonTokenBudget: 0,
          canonCharsPerToken: 1,
        })
        setError(e instanceof Error ? e.message : 'Could not load configurations.')
      })
  }, [])

  useEffect(reload, [reload])

  return (
    <BuilderColumns
      left={
        <aside className="builder-col">
          <div className="tree">
            <div className="tree-section">
              <div className="tree-head">
                <h3>Setup</h3>
              </div>

              <ul className="template-list">
                {SECTIONS.map((entry) => (
                  <li key={entry.value}>
                    <button
                      type="button"
                      className={entry.value === current ? 'selected' : ''}
                      onClick={() => navigate(toSetupPath(entry.value))}
                    >
                      {entry.label}
                      <span className="dim"> · {entry.hint}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </div>

            {list && (
              <div className="tree-section">
                <div className="tree-head">
                  <h3>Live now</h3>
                </div>
                {/* What the loop is obeying, not what a row says. With no configuration active
                    these come from the engine's own fallback, and a panel that showed nothing
                    would imply the server had no starting room at all. */}
                <p className="dim">
                  Starts at <code>{list.activeStartingRoomKey || '—'}</code>
                </p>
                <p className="dim">{list.activeWelcomeMessage}</p>
              </div>
            )}
          </div>
        </aside>
      }
      main={
        <main className="builder-col">
          {error && <p className="bad">{error}</p>}

          {current === 'configurations' && <ConfigurationsPanel list={list} onChanged={reload} />}
          {current === 'moderation' && <ModerationPanel />}
          {current === 'transfer' && <TransferPanel onImported={reload} />}
          {current === 'tokens' && <TokensPanel />}
        </main>
      }
    />
  )
}

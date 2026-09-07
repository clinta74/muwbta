import { useEffect, useState } from 'react'
import { api, mapSheetUrl, type MapSheet } from '../net/api'

/**
 * The drawn maps, for anyone.
 *
 * Sits over the game the way the builder does, and for the same reason: the game screen stays
 * mounted underneath so its SSE stream is never closed and reopened just because somebody wanted
 * to look at where they are.
 */
export function MapShell({
  currentWorld,
  onClose,
}: {
  /**
   * The realm the character is standing in, so the map opens on it rather than on whichever
   * sheet sorts first. Null before the first room arrives.
   */
  currentWorld: string | null
  onClose: () => void
}) {
  const [sheets, setSheets] = useState<MapSheet[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [chosen, setChosen] = useState<string | null>(null)

  /**
   * Fit the width, or draw at the size it was rendered.
   *
   * The sheets are five times taller than they are wide - Ossara is 1105 x 5520 - so fitting the
   * width is the only default that shows a realm as a shape rather than as a column. It is also
   * the one that makes the lettering along the roads too small to read on a phone, which is what
   * the other setting is for: at full size the frame scrolls both ways and a road name is a road
   * name again.
   */
  const [fit, setFit] = useState(true)

  useEffect(() => {
    void api
      .maps()
      .then(setSheets)
      .catch(() => setFailed(true))
  }, [])

  // Held rather than derived, so choosing another realm survives the character walking into a
  // new one. Falling through `currentWorld` and then the first sheet means there is always
  // something drawn the moment the list arrives.
  const sheet =
    sheets?.find((s) => s.world === chosen) ??
    sheets?.find((s) => s.world === currentWorld) ??
    sheets?.[0] ??
    null

  return (
    <div className="map-shell">
      <div className="map-topbar">
        <h2 className="map-brand">Maps</h2>

        {sheets && sheets.length > 1 && (
          <nav className="map-realms" aria-label="Realms">
            {sheets.map((s) => (
              <button
                key={s.world}
                type="button"
                className={s.world === sheet?.world ? 'map-realm current' : 'map-realm'}
                aria-current={s.world === sheet?.world ? 'true' : undefined}
                onClick={() => setChosen(s.world)}
              >
                {s.title}
                {/* Where you actually are, which is not always the sheet being looked at. */}
                {s.world === currentWorld && <span className="map-here"> · here</span>}
              </button>
            ))}
          </nav>
        )}

        <div className="map-topbar-right">
          {sheet && (
            <button type="button" onClick={() => setFit((f) => !f)} aria-pressed={!fit}>
              {fit ? 'Full size' : 'Fit width'}
            </button>
          )}

          <button type="button" className="map-exit" onClick={onClose}>
            Back to the game
          </button>
        </div>
      </div>

      <div className="map-frame">
        {failed && <p className="bad">The maps could not be loaded.</p>}
        {!failed && sheets === null && <p className="dim">Loading…</p>}
        {sheets?.length === 0 && <p className="dim">This server carries no maps.</p>}

        {sheet && (
          /*
           * An <img>, not an inline <svg>. The sheet is a 150 KB document of several thousand
           * elements, and putting it in the page would mean parsing all of it into the same DOM
           * the transcript lives in, with its ids and its filter definitions loose in the
           * document. As an image the browser decodes it off-thread, caches it against the
           * server's ETag, and the sheet keeps its own coordinate space.
           *
           * The intrinsic size is given so the frame does not reflow when the sheet arrives.
           * These are very tall, and a late resize of that throws the scroll position away.
           */
          <img
            key={sheet.world}
            className="map-sheet"
            data-fit={fit ? 'width' : 'none'}
            src={mapSheetUrl(sheet.world)}
            width={sheet.width}
            height={sheet.height}
            alt={`Map of ${sheet.title}`}
          />
        )}
      </div>
    </div>
  )
}

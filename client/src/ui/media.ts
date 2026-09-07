import { useEffect, useState } from 'react'

/**
 * The bits of `matchMedia` the app needs, with the environment's absence handled once.
 *
 * jsdom does not implement `matchMedia`, and the whole suite runs there. Throwing — or worse,
 * reading `undefined.matches` — would take down every component that asks a media question, in an
 * environment where the desktop answer is the right one anyway.
 */

/** Read once, outside React, for the cases that need an answer before first paint. */
export function matchesMedia(query: string): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia(query).matches
}

/**
 * The same answer as state, kept current.
 *
 * These do change while the page is open: rotating a phone, splitting a tablet's screen, plugging
 * in a mouse, or a browser's device emulation — which is how most of this gets tested. Reading
 * once on mount is the bug where the UI is stuck in whichever mode the page happened to load in.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() => matchesMedia(query))

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return

    const list = window.matchMedia(query)
    const sync = (event: MediaQueryListEvent) => setMatches(event.matches)

    // Re-read on mount as well as subscribing: the initial value was computed during the first
    // render, and under StrictMode that is not necessarily this moment.
    setMatches(list.matches)
    list.addEventListener('change', sync)
    return () => list.removeEventListener('change', sync)
  }, [query])

  return matches
}

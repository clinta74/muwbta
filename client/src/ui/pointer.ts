import { matchesMedia, useMediaQuery } from './media'

/**
 * Whether the primary input is a finger rather than a mouse.
 *
 * Deliberately about the *pointer*, not the screen width. A narrow desktop window is still driven
 * by a keyboard and a mouse and should keep the keyboard features; a tablet at 1024px is not. The
 * two questions get confused constantly because they usually agree, and the places they disagree
 * are exactly the ones that feel broken.
 *
 * `(hover: none)` is included because `pointer: coarse` alone is true of some touch-capable
 * laptops, which do have a mouse and should keep behaving like desktops. Requiring both means
 * "a finger, and no mouse to fall back on".
 */
const COARSE = '(pointer: coarse) and (hover: none)'

/**
 * The width at which the game switches to the phone layout — the transcript taking the screen and
 * the room panel moving into a sheet.
 *
 * A width and not the pointer query above, because this one really is about how much room there
 * is: a narrow desktop window has the same problem a phone does, and the layout that solves it is
 * no worse there. The pointer query decides behaviour; this decides shape.
 *
 * `GameScreen` stamps the answer onto the game element as `data-layout`, so the stylesheet keys off
 * that attribute rather than repeating the number.
 */
const PHONE = '(max-width: 600px)'

/**
 * Below this the builder drops to a single pane, the tree becomes a drawer, and the zone canvas
 * stops being on screen at all until it is asked for.
 *
 * Wider than the game's phone breakpoint on purpose. The game needs a phone layout only when the
 * screen is genuinely phone-sized; the builder is a three-pane editor and runs out of room a good
 * deal sooner — a 700px window has nowhere to put a tree, a canvas, and a properties panel.
 */
const COMPACT_BUILDER = '(max-width: 768px)'

export function isCoarsePointer(): boolean {
  return matchesMedia(COARSE)
}

export function useCompactBuilder(): boolean {
  return useMediaQuery(COMPACT_BUILDER)
}

export function useCoarsePointer(): boolean {
  return useMediaQuery(COARSE)
}

export function usePhoneLayout(): boolean {
  return useMediaQuery(PHONE)
}

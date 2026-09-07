/**
 * Whether a keystroke that landed somewhere other than the command input should be pulled into
 * it.
 *
 * A MUD is typed at continuously, so any click - on the map, on a room keyword, on the scrollback
 * to read something - silently disarms the keyboard until the player notices and clicks back. The
 * fix is the one every terminal-shaped web app uses: listen at the document, and on a keystroke
 * that is obviously meant as typing, move focus to the input.
 *
 * The redirect deliberately does not re-inject the character. Focus moves during `keydown`, which
 * is before the browser performs the insertion, so the character lands in the newly focused input
 * on its own. Calling `preventDefault` and appending it by hand would double it up on every
 * browser that behaves correctly.
 *
 * What is excluded matters more than what is included, because each exclusion is a key that
 * already means something where the player pressed it:
 *
 * - Anything with Ctrl, Meta or Alt. Ctrl+C on selected scrollback is the obvious one; refresh,
 *   find and the address bar are the rest.
 * - Keys pressed inside another text field, so the login form and every builder input keep them.
 * - Enter and Space, which activate a focused button. Stealing those would break the Leave and
 *   Open-builder buttons for keyboard users, and no command has ever begun with either.
 * - Tab, Escape, arrows, and the function keys, by virtue of only single-character keys counting.
 *   Tab in particular is the one key that must never be redirected: taking it would trap focus in
 *   the input and make the page unnavigable without a mouse.
 * - Anything mid-composition, so an IME candidate window is not yanked out from under the player.
 */
export function shouldRedirectToInput(event: KeyboardEvent): boolean {
  if (event.ctrlKey || event.metaKey || event.altKey) return false
  if (event.isComposing) return false

  const target = event.target as HTMLElement | null
  if (target && isTextEntry(target)) return false

  // Backspace counts as typing: a player correcting a typo has already lost the keystroke that
  // matters, and a stray one on an empty input does nothing.
  if (event.key === 'Backspace') return true

  return event.key.length === 1 && event.key !== ' '
}

function isTextEntry(element: HTMLElement): boolean {
  if (element.isContentEditable) return true

  const tag = element.tagName.toLowerCase()
  return tag === 'input' || tag === 'textarea' || tag === 'select'
}

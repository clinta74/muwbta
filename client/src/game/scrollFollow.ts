/**
 * Whether the scrollback is close enough to the bottom to count as following it.
 *
 * Following is the normal state, and it is the state a player leaves by scrolling up to read
 * something. The scrollback used to jump to the newest line unconditionally, which meant reading
 * back through a fight that was still going yanked you away four times a second.
 *
 * The slack is the whole rule, and 24px was far too tight. A single arriving line is taller than
 * that, so any reading taken while the view had not caught up with the newest line counted as
 * "the player has scrolled away" and the transcript stopped following - the drift. Leaving is now
 * something a player has to mean: five lines of deliberate scrolling back, rather than one line's
 * worth of the page moving underneath them.
 */
export function isAtBottom(
  box: { scrollTop: number; scrollHeight: number; clientHeight: number },
  slack: number = DEFAULT_SLACK_PX,
): boolean {
  return box.scrollHeight - box.scrollTop - box.clientHeight <= slack
}

/** How far back counts as having left, in lines of text. */
export const FOLLOW_SLACK_LINES = 5

/** `line-height: 1.5` on `.scrollback`, used when the real one cannot be read. */
const FALLBACK_LINE_HEIGHT_REM = 1.5

const FALLBACK_ROOT_FONT_PX = 16

const DEFAULT_SLACK_PX = FOLLOW_SLACK_LINES * FALLBACK_LINE_HEIGHT_REM * FALLBACK_ROOT_FONT_PX

/**
 * The slack in pixels, measured from the element's own text rather than hard-coded.
 *
 * Five lines is a statement about reading, not about pixels, so it has to survive a browser zoom,
 * a different root font size, and any later change to the scrollback's line height. Reading the
 * computed style keeps all three in step with the CSS for free; the constants above are only for
 * when there is no layout to ask, which is every test in jsdom.
 */
export function followSlack(box: Element | null): number {
  if (!box) return DEFAULT_SLACK_PX

  const lineHeight = pixels(getComputedStyle(box).lineHeight)
  if (lineHeight !== null) return FOLLOW_SLACK_LINES * lineHeight

  // `normal` is a real answer and not a number, so fall back through the root font size.
  const root = pixels(getComputedStyle(document.documentElement).fontSize)
  const rem = root ?? FALLBACK_ROOT_FONT_PX

  return FOLLOW_SLACK_LINES * FALLBACK_LINE_HEIGHT_REM * rem
}

function pixels(value: string): number | null {
  const parsed = Number.parseFloat(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

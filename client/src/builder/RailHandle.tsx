import { useRef } from 'react'

interface Props {
  /** Which edge this handle drags: the left rail's right edge, or the right rail's left edge. */
  side: 'left' | 'right'
  /** The rail's current width in pixels, so the keyboard can nudge from where it is. */
  width: number
  onResize: (px: number) => void
  onReset: () => void
}

/** How far one arrow-key press moves a rail. Big enough to be worth pressing, small enough to aim. */
const NUDGE = 16

/**
 * The draggable divider between a builder rail and the editor.
 *
 * <b>Pointer Events rather than mouse events</b>, so this works with a trackpad, a touchscreen and
 * a pen from one code path — the same choice the zone canvas made, and the reason Phase 7's M4a
 * wanted this work grouped with it.
 *
 * <b>Capture on the handle</b>, so a drag that outruns the pointer keeps tracking. Without it the
 * divider stops the moment the cursor leaves the two-pixel target, which at any real dragging
 * speed is immediately.
 *
 * <b>Reachable without a pointer at all.</b> It is a separator with arrow keys and a Home reset,
 * because a control that only responds to dragging is one a keyboard user cannot reach — and the
 * whole point of the feature is that the shipped widths do not suit everybody.
 */
export function RailHandle({ side, width, onResize, onReset }: Props) {
  const dragging = useRef(false)

  return (
    <div
      className={`rail-handle rail-handle-${side}`}
      role="separator"
      aria-orientation="vertical"
      aria-label={side === 'left' ? 'Resize the list' : 'Resize the side panel'}
      aria-valuenow={Math.round(width)}
      tabIndex={0}
      onPointerDown={(e) => {
        dragging.current = true
        e.currentTarget.setPointerCapture?.(e.pointerId)
      }}
      onPointerMove={(e) => {
        if (!dragging.current) return

        // Measured from the window edge rather than by accumulating deltas. Deltas drift when a
        // move is coalesced or the pointer leaves and returns; an absolute read cannot.
        const px = side === 'left' ? e.clientX : window.innerWidth - e.clientX
        onResize(px)
      }}
      onPointerUp={(e) => {
        dragging.current = false
        e.currentTarget.releasePointerCapture?.(e.pointerId)
      }}
      onDoubleClick={onReset}
      onKeyDown={(e) => {
        // Left always means "make the left rail smaller", which for the right-hand rail is the
        // opposite sign - the key names a direction on screen, not a number going up.
        const towardsWider = side === 'left' ? 'ArrowRight' : 'ArrowLeft'
        const towardsNarrower = side === 'left' ? 'ArrowLeft' : 'ArrowRight'

        if (e.key === towardsWider) {
          onResize(width + NUDGE)
        } else if (e.key === towardsNarrower) {
          onResize(width - NUDGE)
        } else if (e.key === 'Home') {
          onReset()
        } else {
          return
        }

        e.preventDefault()
      }}
    />
  )
}

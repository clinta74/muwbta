import { useEffect, useRef } from 'react'

interface TextareaProps {
  value: string
  onChange: (value: string) => void
  rows?: number
  /**
   * How tall it may grow before it scrolls instead, in rows. Omit for no ceiling.
   *
   * Growing without one is right for a room description, which is a few paragraphs and is easier
   * to read whole than through a slot. It is wrong for anything unbounded: a canon runs to tens of
   * thousands of characters, and a field that tall buries every control under it and turns the
   * panel into one enormous scroll.
   */
  maxRows?: number
  placeholder?: string
  disabled?: boolean
  spellCheck?: boolean
  /**
   * Shown, not edited. Selects its whole content on focus, because the only reason to render a
   * value nobody may change in a field at all is so it can be copied out of one.
   */
  readOnly?: boolean
}

/**
 * A multi-line text field that grows to fit its content, so a long room description is not
 * trapped behind a tiny scrollbar. Themed to match the other inputs.
 */
export function Textarea({
  value,
  onChange,
  rows = 3,
  maxRows,
  placeholder,
  disabled,
  spellCheck,
  readOnly,
}: TextareaProps) {
  const ref = useRef<HTMLTextAreaElement>(null)

  // Resize to content on every value change, including external resets.
  useEffect(() => {
    const el = ref.current
    if (!el) return

    el.style.height = 'auto'

    const style = getComputedStyle(el)

    // Everything is border-box, and scrollHeight counts padding but not borders — so a height set
    // from scrollHeight alone is short by the border and leaves a permanent two-pixel scroll.
    const border = (parseFloat(style.borderTopWidth) || 0) + (parseFloat(style.borderBottomWidth) || 0)
    const content = el.scrollHeight + border

    // `font: inherit`, so the line height is whatever the panel's is; jsdom and `normal` both
    // report something parseFloat cannot use, hence the fallback.
    const lineHeight = parseFloat(style.lineHeight) || (parseFloat(style.fontSize) || 16) * 1.4
    const padding = (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0)
    const ceiling = maxRows === undefined ? Infinity : maxRows * lineHeight + padding + border

    const capped = content > ceiling

    el.style.height = `${capped ? ceiling : content}px`

    // Only the capped field keeps a scrollbar. One on a field sized to its own content is a
    // scrollbar with nowhere to go.
    el.style.overflowY = capped ? 'auto' : 'hidden'
  }, [value, maxRows])

  return (
    <textarea
      ref={ref}
      className="textarea"
      rows={rows}
      value={value}
      placeholder={placeholder}
      disabled={disabled}
      spellCheck={spellCheck}
      readOnly={readOnly}
      onFocus={readOnly ? (e) => e.target.select() : undefined}
      onChange={(e) => onChange(e.target.value)}
    />
  )
}

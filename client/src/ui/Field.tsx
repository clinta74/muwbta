import type { ReactNode } from 'react'

/**
 * How much room this field's content actually needs.
 *
 * <b>A union rather than a class name, for the reason <see cref="Button"/> gives.</b> A width is a
 * decision about the content — "this holds one glyph", "this holds a level" — and a decision that
 * can be checked should be. The scale itself lives in `styles/components/_field.scss`.
 *
 * The small sizes do not grow when the row has slack; `md` and `lg` do. That is what makes a row
 * of [icon, level, name] come out as three fields of sensible width with the name taking the rest,
 * instead of three equal thirds.
 */
export type FieldWidth = 'char' | 'xs' | 'sm' | 'md' | 'lg' | 'full'

interface FieldProps {
  label: ReactNode
  /** Quiet helper text under the control. */
  hint?: ReactNode
  /** Inline validation message, shown in the error colour when present. */
  error?: string | null
  /**
   * Defaults to `md`, which is what an unsized field was already getting. Set it when the content
   * is narrower than a word — a one-character icon, a two-digit level — or wider, like an entity
   * key.
   */
  width?: FieldWidth
  children: ReactNode
}

/**
 * A labelled control: label above, control, then an optional hint and inline error. Collapses
 * the ~40 hand-written `<label>Foo<input/></label>` blocks scattered through the editors into
 * one consistent shape.
 */
export function Field({ label, hint, error, width, children }: FieldProps) {
  const className = width ? `field field-w-${width}` : 'field'

  return (
    <label className={className}>
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
      {error && <span className="field-error">{error}</span>}
    </label>
  )
}

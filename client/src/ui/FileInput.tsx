import type { Ref } from 'react'

interface FileInputProps {
  /** Called with the file the moment one is chosen. Never called with nothing. */
  onFile: (file: File) => void
  /** Passed straight through, e.g. `application/json,.json`. */
  accept?: string
  disabled?: boolean
  /** So the owner can clear the field — a file input's value cannot be set from a prop. */
  ref?: Ref<HTMLInputElement>
  'aria-label'?: string
}

/**
 * A file picker wearing the builder's own frame rather than the operating system's.
 *
 * <b>The native element is kept and styled, not hidden behind a button.</b> The usual dodge —
 * `display: none` on the input and a real `<button>` beside it calling `.click()` — throws away
 * the element's semantics and then has to rebuild each one by hand: the label association, the
 * focus ring, the announced file name, the drop target. `::file-selector-button` restyles the one
 * part that looked out of place and leaves the rest of the control alone.
 *
 * The unstyled input this replaces was the only control on the Setup tab still rendering in the
 * OS's own chrome — grey, differently sized in every browser, next to a column of themed fields.
 */
export function FileInput({ onFile, accept, disabled, ref, ...rest }: FileInputProps) {
  return (
    <input
      ref={ref}
      type="file"
      className="file-field"
      accept={accept}
      disabled={disabled}
      aria-label={rest['aria-label']}
      onChange={(e) => {
        const file = e.target.files?.[0]
        if (file) onFile(file)
      }}
    />
  )
}

import type { FlagValue, RoomFlagDefinition } from '../../net/builderApi'

interface Props {
  definition: RoomFlagDefinition
  /** What this level declares itself, or undefined when it inherits. */
  own: FlagValue | undefined
  disabled?: boolean
  inheritTitle: string
  onSet: (value: FlagValue | null) => void
}

/**
 * The three-state control for one flag, in whichever shape its kind needs.
 *
 * Shared by the room panel and the world/zone panel, which had two copies of the boolean version
 * already and would have grown two copies of the text one. The three states are the same either
 * way and are the point: "off" is a decision about this level, while "inherit" removes the key so
 * the level above decides, and a two-state control cannot tell those apart.
 *
 * A text flag renders as a list rather than a text box because its choices are closed - the server
 * refuses a value that is not one of them, so offering a free text field would be offering a way
 * to be told no.
 */
export function FlagControl({ definition, own, disabled, inheritTitle, onSet }: Props) {
  const declared = own !== undefined

  if (definition.kind === 'text') {
    return (
      <div className="flag-controls">
        <select
          className={declared ? 'selected' : ''}
          value={declared ? String(own) : ''}
          disabled={disabled}
          onChange={(e) => onSet(e.target.value === '' ? null : e.target.value)}
        >
          <option value="">inherit</option>
          {definition.choices.map((choice) => (
            <option key={choice} value={choice}>
              {choice}
            </option>
          ))}
        </select>
      </div>
    )
  }

  return (
    <div className="flag-controls">
      <button
        type="button"
        disabled={disabled}
        className={declared && own === true ? 'selected' : ''}
        onClick={() => onSet(true)}
      >
        on
      </button>
      <button
        type="button"
        disabled={disabled}
        className={declared && own === false ? 'selected' : ''}
        onClick={() => onSet(false)}
      >
        off
      </button>
      <button
        type="button"
        disabled={disabled}
        className={!declared ? 'selected' : ''}
        onClick={() => onSet(null)}
        title={inheritTitle}
      >
        inherit
      </button>
    </div>
  )
}

/** How a flag's value reads in the header - "on"/"off" for a boolean, the word itself for text. */
export function flagLabel(value: FlagValue | undefined): string {
  if (value === undefined) {
    return 'inherited'
  }

  return typeof value === 'boolean' ? (value ? 'on' : 'off') : value
}

/** Whether a value should read as the "set" colour. A text flag is never grey for being false. */
export function flagIsPositive(value: FlagValue | undefined): boolean {
  return typeof value === 'boolean' ? value : value !== undefined
}

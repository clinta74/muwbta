/**
 * Re-flows a room description so the window decides where lines end, not the author's editor.
 *
 * A description is prose, and prose that arrives hard-wrapped renders as a ragged column with its
 * breaks in whatever place they happened to fall when somebody typed it - narrower than the
 * window on a desktop, and in the wrong places on a phone. The transcript is `white-space:
 * pre-wrap`, which is right for everything else in a line (each of `Exits:`, an occupant, a mob
 * lives on its own `\n`) and wrong for this one span, because it honours those accidental breaks
 * exactly as faithfully as the deliberate ones.
 *
 * So single newlines collapse to a space and **blank lines survive as paragraph breaks**. That
 * distinction is the whole function: a break somebody typed to end a paragraph means something,
 * and a break that is only where the line ran out does not.
 *
 * Leading newlines are kept as they are. The span arrives prefixed with the `\n` that separates
 * it from the room title, and trimming that away would put the description on the title's line.
 */
export function reflow(text: string): string {
  const lead = /^\n*/.exec(text)?.[0] ?? ''

  const body = text
    .slice(lead.length)
    .split(/\n[ \t]*\n\s*/)
    .map((paragraph) => paragraph.replace(/\s*\n\s*/g, ' ').trim())
    .filter((paragraph) => paragraph !== '')
    .join('\n\n')

  return lead + body
}

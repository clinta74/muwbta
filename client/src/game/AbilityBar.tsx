import { useEffect, useLayoutEffect, useRef, useState, type WheelEvent } from "react";
import { PULSE_MS, type AbilityEntry } from "../net/protocol";

interface Props {
  abilities: AbilityEntry[];
  /** When each cooling ability becomes usable again, as a wall-clock timestamp. */
  cooldownUntil: Record<string, number>;
}

/**
 * How often the countdown redraws. A quarter second is one pulse, which is as fine as the server
 * can distinguish anyway - a faster timer would render a bar the game does not have.
 */
const TICK_MS = PULSE_MS;

/** Cells in a cooldown bar. Ten, because that is what a vitals meter uses. */
const CELLS = 10;

/**
 * Which edges of the row have chips hidden past them, as the value of `data-more` on the bar.
 * A string rather than an object so that re-measuring to the same answer is a no-op for React.
 */
function hiddenEdges(row: HTMLElement): string {
  const slack = 1; // sub-pixel scroll positions round either way
  const left = row.scrollLeft > slack;
  const right = row.scrollLeft < row.scrollWidth - row.clientWidth - slack;
  return [left && "left", right && "right"].filter(Boolean).join(" ");
}

/**
 * What is still cooling down, and nothing else.
 *
 * <b>Only cooling abilities appear.</b> The bar used to list every ability the character had, which
 * at level 45 is a row wider than the screen with a horizontal scrollbar under it - so the one
 * thing worth seeing at a glance, what is not ready yet, was the thing you had to scroll to find.
 * A short row that empties itself says more than a long one that never changes. What the character
 * <em>can</em> do is answered by the `abilities` command, and by the level-up message that names
 * each new one as it is earned.
 *
 * The cost of that is the chips no longer fill the input, since there is nothing to click while an
 * ability is ready. Deliberate for now: the verb is the interface.
 *
 * <b>The row stays when it is empty.</b> It used to unmount, and every time the last cooldown ran
 * out the transcript grew a row and the input jumped under the cursor - in the middle of a fight,
 * which is the one time the bar is busy. The row keeps its one-chip height (see the `:empty` rule
 * in game.scss) so nothing below it moves.
 *
 * <b>One line, scrolled, never wrapped.</b> A second line is the same jump from the other side.
 * The native scrollbar is hidden and a fade at whichever edge has more past it says so instead;
 * `data-more` carries that, measured here because CSS cannot see overflow.
 *
 * <b>The countdown runs here, not on the wire.</b> The server sends one event when an ability
 * fires and nothing after; this derives the bar on screen from a stored instant and the clock. The
 * alternative - a frame per pulse per cooling ability - is four a second per player, which is
 * exactly the traffic PLAN.md §11's pulse budget is careful about.
 *
 * <b>The timer only exists while something is cooling.</b> An interval that runs forever on an idle
 * screen is a wakeup every quarter second for a row that is not there, and on a phone that is
 * battery.
 */
export function AbilityBar({ abilities, cooldownUntil }: Props) {
  const [now, setNow] = useState(() => Date.now());
  const [more, setMore] = useState("");
  const rowRef = useRef<HTMLUListElement>(null);

  const cooling = abilities.filter(
    (ability) => (cooldownUntil[ability.key] ?? 0) > now,
  );
  const idle = cooling.length === 0;

  useEffect(() => {
    if (idle) return;
    const timer = setInterval(() => setNow(Date.now()), TICK_MS);
    return () => clearInterval(timer);
  }, [idle]);

  // Whenever the set of chips changes, since they come and go without the row itself changing
  // size - which is why a ResizeObserver alone is not enough. Only the set: a chip is the same
  // width from ten cells to one, so the countdown itself never moves an edge. The observer catches
  // the other case, the window narrowing under a row that fitted a moment ago. The typeof guard
  // is for jsdom, which has no observer.
  const coolingKeys = cooling.map((ability) => ability.key).join(",");
  useLayoutEffect(() => {
    const row = rowRef.current;
    if (row) setMore(hiddenEdges(row));
  }, [coolingKeys]);

  useEffect(() => {
    const row = rowRef.current;
    if (!row || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(() => setMore(hiddenEdges(row)));
    observer.observe(row);
    return () => observer.disconnect();
  }, []);

  // A mouse wheel only scrolls sideways with shift held, which nobody is going to guess for a row
  // of chips. The row has no vertical scroll to compete with, so the plain wheel drives it.
  const onWheel = (event: WheelEvent<HTMLUListElement>) => {
    if (event.deltaY !== 0 && event.deltaX === 0) {
      event.currentTarget.scrollLeft += event.deltaY;
    }
  };

  return (
    <div className="ability-bar" data-more={more || undefined}>
      <ul
        ref={rowRef}
        className="ability-bar-row"
        aria-label="Cooling down"
        // A scrollable region a keyboard cannot reach is content a keyboard cannot see; but a tab
        // stop on an empty row is a stop for nothing, so only while there is something past an edge.
        tabIndex={more ? 0 : undefined}
        onScroll={(event) => setMore(hiddenEdges(event.currentTarget))}
        onWheel={onWheel}
      >
        {cooling.map((ability) => {
          const remainingMs = Math.max(0, (cooldownUntil[ability.key] ?? 0) - now);

          // The denominator is the roster's figure rather than the fired event's, because a
          // reconnect resyncs from the roster and only ever learns what is *left*. The two agree by
          // construction today - AbilitySystem sends the ability's own CooldownPulses - and the
          // clamp below is what keeps a builder retuning one mid-cooldown from drawing past full.
          const totalMs = ability.cooldownPulses * PULSE_MS;
          const fraction = totalMs > 0 ? Math.min(1, remainingMs / totalMs) : 1;

          // Rounded up, so the bar keeps a cell right until the ability is actually usable. Rounding
          // to nearest empties it early, on something the server would still refuse.
          const filled = Math.max(1, Math.ceil(fraction * CELLS));
          const seconds = Math.ceil(remainingMs / 1000);

          return (
            <li
              key={ability.key}
              className="ability-chip"
              // The bar is decoration to a screen reader; the seconds are the content. Announced
              // from the label rather than from the bar, because "block block block light shade"
              // is what reading the fill out loud actually sounds like.
              aria-label={`${ability.name}, ${seconds} seconds`}
              title={`${ability.name} — ${seconds}s`}
            >
              <span className="ability-chip-name">{ability.name}</span>
              <span className="ability-chip-bar" aria-hidden="true">
                {"█".repeat(filled)}
                <span className="dim">{"░".repeat(CELLS - filled)}</span>
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

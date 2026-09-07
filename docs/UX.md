# UX evaluation

A review of the browser client — the game screen and the builder — against the six things the
playtest note asked for: best practice, style consistency, letter casing, spacing, layout spacing,
resizability, and whether it is intuitive.

Written 2026-08-13 against 2,457 lines of CSS across three files and 58 components.

**All eight findings are now closed.** The detail below is kept as the record of what each was and
how it was answered — several of the fixes are decisions rather than tidying, and the reasoning is
the part worth having later.

**The summary, before the detail.** The client is in better shape than a codebase of this age
usually is, and the two things most reviews of a hobby project spend their length on — inconsistent
capitalisation and unlabelled controls — are already right here. What it lacks is a *system*: there
are seventeen colour tokens and none for spacing, type, or radius, so every component reaches for a
number and the numbers have drifted apart. One finding is a live defect rather than a matter of
taste, and it is first.

Findings are ordered by what they cost a user, not by effort.

---

## 1. Four destructive buttons have no destructive styling — FIXED

**Severity: defect.** This is not a style opinion; the class does not exist.

Four buttons ask for `className="danger"`:

| File | What it removes |
|---|---|
| `builder/mobs/AttackEditor.tsx:68` | An attack |
| `builder/mobs/LootEditor.tsx:67` | A loot entry |
| `builder/mobs/MobBehaviorEditor.tsx:99` | An idle emote |
| `builder/mobs/MobBehaviorEditor.tsx:229` | A stocked shop item |

The stylesheets define `.editor-section.danger` (a section's top border), `.danger-button`, and
`.menu-item-danger`. There is **no `.danger` rule that applies to a button**, and
`.editor-section.danger` matches the section element rather than any descendant. So all four render
as ordinary buttons, visually identical to *Add emote* sitting beside them.

The elsewhere-correct spelling is `.danger-button`, used in four other places including
`ConfirmDialog`. So the product has two names for one variant and one of them is dead.

**Fixed** by renaming the four to `danger-button`. `.danger` was not added as an alias: two names
for one variant is how this happened, and finding 3 is the real answer.

---

## 2. Resizability: nothing is resizable except one textarea — FIXED

**Severity: high — it is the one axis the note named that the client does not address at all.**

- The builder is `grid-template-columns: 15rem 1fr 17rem`, fixed. There are no drag handles
  anywhere in the product.
- The whole client contains exactly one `resize:` declaration — `resize: vertical` on
  `ui/Textarea.tsx`.

Two places where this bites, both routine:

**Room keys do not fit the tree.** Keys are `world.zone.room`, so
`aldenmoor.millbrook.tavern-common` is 33 characters in a 15rem rail. The follow readout already
concedes this with `max-width: 16rem; text-overflow: ellipsis` — the fix was applied to the symptom
in one place rather than to the cause.

**Room descriptions are the longest prose in the product** and are written in a textarea in the
middle column, which cannot be widened. A four-sentence description is what §7.6 says a builder
should not be typing on a command line; the form it was given instead is a fixed box.

**Fixed** the first way. Both rails drag, on Pointer Events like the zone canvas pan, with the
widths in one `localStorage` entry shared across tabs — a tree widened to fit
`aldenmoor.millbrook.tavern-common` stays wide when you switch to Mobs. Double-click or Home
resets; the handles are `role="separator"` with arrow keys, because a control that only answers to
dragging cannot be reached by the keyboard, and the whole point is that the shipped widths do not
suit everybody.

Hidden below 768px, where the first column is already a drawer over the editor and there is no
boundary to drag.

---

## 3. There is no shared Button, and no tokens for anything but colour — FIXED

**Severity: high — it is the cause of findings 1, 4, and 5.**

`ui/` has `Field`, `Modal`, `Select`, `Textarea`, `NumberInput`, `Tabs`, `Toast`, `ConfirmDialog`,
`OverflowMenu` — a real primitive set, and it is why the forms are as consistent as they are.
**Button is the omission**, and it is the most-used control in the product: 102 raw `<button>`
elements, styled by whichever of `primary`, `danger`, `danger-button`, or nothing the author
reached for.

The token situation is the same shape. Seventeen custom properties are defined, all colour:
`--accent`, `--bad`, `--bg`, `--border`, `--chat`, `--dim`, `--emote`, `--focus`, `--good`,
`--health`, `--movement`, `--panel`, `--party`, `--speech`, `--stamina`, `--tell`, `--text`.
Colour is therefore the one dimension that is consistent everywhere. There are no tokens for
spacing, type scale, or radius — and those are exactly the three dimensions that have drifted.

**Fixed.** `ui/Button.tsx` takes `variant="quiet" | "primary" | "danger" | "link"`, and all 41
variant call sites went through it. Neutral buttons stay bare `<button>` deliberately: the element
rule already styles them, they have no variant to get wrong, and routing a hundred of them through
a wrapper is churn with a regression surface and no defect behind it. What the component removes is
the case where the intent was "destructive" and the output was "ordinary".

`--space-*`, `--text-*` and `--radius-*` now sit beside the colours.

---

## 4. Layout spacing: 24 distinct values, no scale — FIXED

Every padding, margin, and gap in the client, by frequency:

```
0.5rem ×34   0.6rem ×27   1rem ×21    0.4rem ×19   0.75rem ×14
0.35rem ×13  0.3rem ×10   0.9rem ×9   0.2rem ×9    0.45rem ×8
0.25rem ×8   0.8rem ×7    0.15rem ×6  0.1rem ×5    0.7rem ×4
1.5rem ×3    1.25rem ×3   1.1rem ×3   2rem ×2      0.55rem ×2
0.85rem ×1   3rem ×1      + 0px, 1px, 2px
```

The run from `0.35` to `0.9` in steps of `0.05` is the tell. The difference between `0.45rem` and
`0.5rem` is under a pixel at default type size — invisible on screen, and permanent in the CSS.
Nobody chose these against each other; each was chosen against whatever was on screen at the time.

Type is the same story: **19 distinct font sizes**, and they mix units for the same value —
`0.8rem` (9 uses) and `0.8em` (4 uses) both exist, as do `0.85rem` and `0.85em`. A `rem`/`em` split
is meaningful when it is deliberate and a bug when it is not; here `.preview-table` is `0.82rem`
with `thead th` at `0.72rem`, while `.menu-item` is `0.9em`, so the same visual weight is reached
two different ways in two components.

Radius: `3px` ×9, `4px` ×8, `6px` ×7, `8px` ×2, `999px` ×2, `5px` ×1. Three values doing one job.

**Fixed:** the six-step scale went in and 214 declarations across the stylesheets moved onto it,
covering 21 of the distinct values. Two literals remain on purpose — a `3rem` hero padding above
the top step, and a `0.45rem` inside a `calc()` with `env(safe-area-inset-top)`, where a token
would have to be resolved before the addition.

---

## 5. Letter casing is already right — the unit drift is FIXED

**Casing needs no work.** Every one of ~80 `Field` labels is sentence case, buttons and tab labels
agree, and the two-letter vitals (`HP`, `FO`, `ST`) and initialisms (`XP`) are correctly
capitalised against that. This is the axis the note worried about and the one that is finished.

**Units are where the drift is.** Three conventions in one form system:

| Convention | Where |
|---|---|
| Parenthetical | `Attack delay (pulses)`, `Delay (pulses)`, `Wander (pulses)`, `Weight (grams)` |
| Appended word | `Respawn seconds` |
| Hint only | `Every` / `to` with `hint="seconds, at least"` |

**Fixed:** parenthetical everywhere — `Respawn (seconds)`, `Every (seconds)`, `to (seconds)`. Note
this settles the *convention*, not finding 6: three fields still ask a builder for pulses.

---

## 6. Pulses leak into the builder, and a pulse is not a unit anyone thinks in — FIXED

**Severity: medium, and the clearest "intuitive" finding.**

A pulse is 250 ms (§2.3) — an engine implementation detail. It reaches the builder in three fields,
while two adjacent fields use seconds:

- `Attack delay (pulses)`, hinted *"Minimum 4 ≈ 1s"*
- `Delay (pulses)`, hinted *"Minimum 4 ≈ 1s"*
- `Wander (pulses)`, unhinted
- `Respawn seconds`
- Emote `Every` / `to`, in seconds

So a builder authoring one mob converts between two time units inside one editor, and the hint that
makes the conversion possible is present on two of the three pulse fields and missing from the
third. The emote fields already prove the better answer: they take seconds and the engine converts.

**Fix:** take seconds everywhere and convert at the boundary, as `MobEmote.FromSeconds` already
does. Where a pulse floor matters (minimum 4), the hint becomes *"at least 1 second"*. The stored
shape need not change.

---

## 7. Keyboard focus on selects and textareas is a 1px border tint — FIXED

**Severity: medium — accessibility, and it fails quietly.**

`ui/ui.css` contains **zero** `:focus-visible` rules and five `outline: none`. Two of them matter:

```css
.textarea:focus { outline: none; border-color: var(--accent); }
.select:focus   { outline: none; border-color: var(--accent); }
```

The substitute for the removed outline is a one-pixel border colour change, on the two most common
controls in the builder. It is also `:focus` rather than `:focus-visible`, so it fires on mouse
click too — which is the reason it was made subtle, and the reason it is now too subtle to serve as
a keyboard indicator. A keyboard-only builder tabbing through the mob editor cannot reliably tell
which control is focused.

The rest is fine: `.dlg:focus { outline: none }` is correct for a dialog container, `.menu-item`
substitutes a background under Radix's roving focus, and the checkbox rules do use `:focus-visible`
with a real 2px outline. That last one is the pattern the other two should copy.

**Fixed** exactly that way: `:focus-visible` gives the keyboard a real 2px ring, and the quiet
`:focus` border stays for the mouse — which is why it was made subtle in the first place, so the two
uses no longer have to share one treatment.

---

## 8. Done: the *Follow my character* checkbox is gone

Removed, as the note asked. It was a setting with one correct position: the follow effect already
moved you only on an actual move, already stood off a form with unsaved edits, and already left you
alone on a room you had clicked. With all three true there was nothing for the off switch to
protect, so the topbar now shows where the character is standing rather than asking whether you
want to know.

`BuilderOutletContext` loses `follow` and `setFollow`, `App` loses a `useState`, and the smoke test
that covered "does not snap back when follow is on" now covers the same property as an unconditional
one — which is the assertion that mattered either way.

---

## What is already good, and should not be traded away

Worth recording so a later pass does not "fix" it:

- **Colour is fully tokenised**, including semantic channel colours for speech, tells, party, and
  chat. This is why the transcript reads well and why theming would be cheap.
- **The `ui/` primitives are real** and `Field` in particular has already collapsed ~40
  hand-written label blocks into one shape. The forms are consistent *because* of it.
- **Sentence case is universal.** See finding 5.
- **`prefers-reduced-motion` is honoured** in both stylesheets.
- **The compact breakpoints are considered**, not incidental: below 768px the builder's first
  column becomes a drawer with a scrim, and the zone canvas is summoned rather than resident — with
  the reasoning written down beside it.
- **Empty and loading states are written as prose**, not spinners — *"Pick a room, or dig one from
  the room you are standing in."* is a better empty state than most shipped products manage.

---

## Suggested order

1. ~~**Finding 1** — four dead `danger` classes.~~ Done: renamed to `danger-button`.
2. ~~**Finding 7** — focus rings on select and textarea.~~ Done: `:focus-visible` gives the
   keyboard a real 2px ring while a mouse click keeps the quiet border.
3. ~~**Finding 5** — unit convention in labels.~~ Done: parenthetical everywhere.
4. **Finding 3, tokens half** — add the spacing, type, and radius tokens without adopting them.
5. ~~**Finding 2** — resizable builder rails.~~ Done, on Pointer Events and persisted.
6. ~~**Finding 6** — seconds instead of pulses.~~ Done at the input rather than in the API, so no
   field names, rows or bundles changed.
7. ~~**Findings 3 and 4, adoption**~~ Done in the same pass: 214 spacing declarations moved onto
   the scale.

---

# Round two

Written 2026-09-06, against the client the round above left behind. Findings 1–8 were about
decisions the stylesheet had made badly. These are about decisions it never made at all.

**The finding under all of them: the markup had been written against a design vocabulary the
stylesheet never learned.** Twelve class names were used and matched no rule anywhere, and the
worst of them, `.field-row`, was in the markup nineteen times. That is not a stylesheet drifting
from its components; it is components asking for a system that was never finished, and getting the
browser's defaults instead — silently, because a class that matches nothing produces no error.

Round one's finding 1 was the same shape at a smaller scale: four buttons asked for `.danger` and
no such rule existed. What that found in four buttons, this found in four editors and a tab.

---

## 9. `.field-row` had no `display` — FIXED

**Severity: defect.** Nineteen call sites, no rule.

`.field-row` is what puts two or three fields on one line in the item, mob, quest and ability
editors. The only rules that ever named it were `.attack-list .field-row` and
`.behavior-editor .field-row`, and both set nothing but `align-items: flex-end`.

So outside those two containers it was a plain block. Its children stacked, and the global
`input { width: 100% }` gave each one the full rail. An item's icon holds **one character** and was
as wide as its description; a mob's level holds two digits and had a row to itself.

**Fixed** by making it a wrapping flex row and giving `Field` a typed `width` —
`char`/`xs`/`sm`/`md`/`lg`/`full`, a union rather than a class name, for the reason `Button` takes a
`variant`. The *field* carries the size and the control keeps `width: 100%` and fills it, so a size
is declared once and nothing reaches past the field to the input. The small sizes do not take slack
when a row has some; `md` and `lg` do, which is what makes `[icon, name, level]` come out as three
sensible widths rather than three equal thirds.

---

## 10. Four of the twelve orphan classes were in one panel — FIXED

**Severity: defect.** `TokensPanel` asked for `.form-grid`, `.grid`, `.callout` and `.ghost`, and
got four unstyled elements.

The `.grid` one is worth naming precisely, because it was reported as *"the padding between scope
and name is not matched and too small"*: there was no padding rule. The table was rendering at the
user agent's default, so the gap was whatever the browser chose, and it was not chosen against
anything else on the screen.

`.ghost` was on two buttons, one of them **Revoke** — the only irreversible control on the panel,
rendering as an ordinary button. Same defect as round one's finding 1, in a panel written after it
was closed.

**Fixed** by adding the three that are real primitives — `.callout`, and `.data-table` in place of
`.grid` — and *deleting* `.ghost` rather than defining it. A quiet button is the bare `<button>`
and a destructive one is `Button variant="danger"`; adding a third spelling is how the dead
`.danger` survived the first time.

The revoke `confirm()` became a `ConfirmDialog` in the same pass, and the file picker in
`TransferPanel` — the last control in the builder still rendering in the operating system's own
chrome — became `.file-field`, styled through `::file-selector-button` so the element keeps its own
semantics rather than being hidden behind a button that has to rebuild them.

---

## 11. One rule, three names — FIXED

`.multiplier-set`, `.attack-list` and `.behavior-editor` were byte-identical declarations sharing a
legend rule. Which one an editor reached for was down to which was written first: the item editor
groups its stats in a `.multiplier-set`, the quest editor groups its rewards in a
`.behavior-editor`, and neither has anything to do with multipliers or behaviour.

**Fixed:** one `.subpanel`. Round one found two names for one variant and called that the cause;
this is the same thing with three.

Two smaller ones went with it. `.preview-table` was the only good table in the product and was
scoped to one panel — it is `.data-table.dense` now. And `.stat-grid` used `auto-fit`, which
collapses the tracks it did not use and shares the width among the rest, so a group of *two* stats
rendered as two half-rail boxes for two-digit numbers; `auto-fill` keeps the columns the width the
content wants however many there are.

---

## 12. `h3` and `h4` had no rules — FIXED

`h1` and `h2` did. So every panel title and every sub-heading in the builder rendered at the
browser's default — bold, 1.17em, a full em of margin — beside headings that had been given a
deliberate size. Two levels are what the panels actually use: `h3` names the panel, `h4` names a
block inside it, and `h4` takes the uppercase-dim treatment the field labels, the legends and
`.tree-head h3` already shared.

---

## 13. App.css was three stylesheets — FIXED

2,053 lines holding the design tokens, the whole game client, and a second builder stylesheet —
the latter under a comment in the file itself saying those rules belonged in `builder.css`. A
change to a spacing token and a change to the transcript were edits to the same file.

**Fixed** by moving to SCSS and splitting: `styles/` is tokens, base element rules, and one partial
per component; `builder/builder.scss` and `game/game.scss` carry what is theirs and nothing another
feature could want. `main.tsx` imports the system before anything else, because CSS lands in the
bundle in module-evaluation order and a feature sheet must not get ahead of the base rules it sits
on.

The move is rule-block for rule-block. A selector-set diff over the four old files and the sixteen
new ones reports only the renames in findings 10 and 11 — worth re-running if this is ever done
again, because it is the only thing that makes a 2,000-line move reviewable.

`src/components/` went at the same time. It held the game screen, the sign-in screen, ten loose
logic modules and two shared hooks, under a name that says only "not the builder" — and it was
where `game.scss` would have had to live, beside the components it styles and under a folder named
after none of them.

---

## 14. Tests sat wherever the component they rendered first happened to live — FIXED

31 of the 45 test files named the module beside them, which is the convention that works. The other
14 did not: six `*.smoke.test.tsx` files drove several tabs each from inside one tab's folder,
`changeFeed.test.tsx` sat in `builder/` next to no `changeFeed`, and `railWidths.test.ts` and
`heartbeat.test.ts` were named after subjects that do not exist as files.

**Fixed** in two tiers. A test about one module stays beside it. A test about several components
together moves to `client/tests/`, mirroring the feature — with a `@/` alias so a moved test does
not open with a run of `../../src/`. The two misnamed unit tests were renamed rather than moved,
because the pairing is the point: `useRailWidths.test.ts` and `stream.heartbeat.test.ts`.

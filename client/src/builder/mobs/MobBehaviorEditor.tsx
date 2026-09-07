import type { ItemTemplate } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { NumberInput } from '../../ui/NumberInput'
import { Select } from '../../ui/Select'
import { TemplatePicker } from '../templates/TemplatePicker'
import {
  DEFAULT_EMOTE_MAX_SECONDS,
  DEFAULT_EMOTE_MIN_SECONDS,
  DISPOSITIONS,
  shopPrice,
  type BehaviorDraft,
  type Disposition,
  type EmoteDraft,
  type TopicDraft,
} from './behavior'

interface Props {
  draft: BehaviorDraft
  itemTemplates: ItemTemplate[]
  onChange: (next: BehaviorDraft) => void
}

/**
 * Edits the parts of a mob's behavior the engine reads (PLAN.md §9.4).
 *
 * Before this the whole bag was passed through a save untouched, so a shopkeeper or an NPC could
 * only be created by writing JSON into the database by hand - which meant §5.2c shops had no
 * authoring path at all.
 */
export function MobBehaviorEditor({ draft, itemTemplates, onChange }: Props) {
  const set = (patch: Partial<BehaviorDraft>) => onChange({ ...draft, ...patch })

  const editEmote = (index: number, patch: Partial<EmoteDraft>) =>
    set({ emotes: draft.emotes.map((e, i) => (i === index ? { ...e, ...patch } : e)) })

  const stocked = new Set(draft.sells)
  const unstocked = itemTemplates.filter((t) => !stocked.has(t.key))

  return (
    <fieldset className="subpanel">
      <legend>Behavior</legend>

      <Field
        label="Disposition"
        hint={DISPOSITIONS.find((d) => d.value === draft.disposition)?.hint}
      >
        <Select
          value={draft.disposition}
          onChange={(v) => set({ disposition: v as Disposition })}
        >
          {DISPOSITIONS.map((d) => (
            <option key={d.value} value={d.value}>
              {d.label}
            </option>
          ))}
        </Select>
      </Field>

      {draft.disposition === 'npc' && (
        <p className="dim">
          Quest givers, quest turn-ins, and shopkeepers should all be NPCs. A killable quest
          giver strands anyone mid-quest until it respawns. They also want{' '}
          <strong>Wanders between rooms</strong> left off, which is the default.
        </p>
      )}

      <div className="emote-list">
        <span className="field-label">Idle emotes</span>
        <p className="dim">
          Shown as “{'{name}'} {draft.emotes[0]?.text || 'snarls'}.” Each line keeps its own
          clock and picks a fresh gap inside its range every time, so two of the same mob in a
          room do not fall into step. Leave the list empty and this mob stands in silence.
        </p>

        {draft.emotes.map((emote, index) => (
          <div className="field-row" key={index}>
            <Field label="Says" width="lg">
              <input
                value={emote.text}
                placeholder="scratches at the floor"
                onChange={(e) => editEmote(index, { text: e.target.value })}
              />
            </Field>
            <Field label="Every" width="xs" hint="Seconds, at least">
              <NumberInput
                min={1}
                value={emote.minSeconds}
                onChange={(v) => editEmote(index, { minSeconds: v })}
              />
            </Field>
            <Field label="to" width="xs" hint="Seconds, at most">
              <NumberInput
                min={1}
                value={emote.maxSeconds}
                onChange={(v) => editEmote(index, { maxSeconds: v })}
              />
            </Field>
            <Button
              variant="danger"
              onClick={() => set({ emotes: draft.emotes.filter((_, i) => i !== index) })}
            >
              Remove
            </Button>
          </div>
        ))}

        {draft.emotes.some((e) => e.maxSeconds < e.minSeconds) && (
          <p className="bad">
            A range that ends before it starts is read as exactly the lower number.
          </p>
        )}

        <button
          type="button"
          onClick={() =>
            set({
              emotes: [
                ...draft.emotes,
                {
                  text: '',
                  minSeconds: DEFAULT_EMOTE_MIN_SECONDS,
                  maxSeconds: DEFAULT_EMOTE_MAX_SECONDS,
                },
              ],
            })
          }
        >
          Add emote
        </button>
      </div>

      <div className="emote-list">
        <span className="field-label">Greetings</span>
        <p className="dim">
          What this mob says when a player uses <strong>talk</strong> on it. Answered in turn, so
          two lines means the second visit reads differently. A quest giver with something to offer
          says that instead; a shopkeeper with no lines here is pointed at their own counter.
          Leave it empty and an ordinary NPC gets a stock line.
        </p>

        {draft.greeting.map((line, index) => (
          <div className="field-row" key={index}>
            <Field label="Says">
              <input
                value={line}
                placeholder="'Mind the step.'"
                onChange={(e) =>
                  set({
                    greeting: draft.greeting.map((g, i) => (i === index ? e.target.value : g)),
                  })
                }
              />
            </Field>
            <Button
              variant="danger"
              onClick={() => set({ greeting: draft.greeting.filter((_, i) => i !== index) })}
            >
              Remove
            </Button>
          </div>
        ))}

        <button type="button" onClick={() => set({ greeting: [...draft.greeting, ''] })}>
          Add greeting
        </button>
      </div>

      <div className="emote-list">
        <span className="field-label">Topics</span>
        <p className="dim">
          What this mob can be asked about: <strong>talk &lt;npc&gt; &lt;keyword&gt;</strong>{' '}
          answers with the text. Mark a word in a greeting or an answer with angle brackets, like{' '}
          <code>&lt;stone&gt;</code>, and it becomes a link to the topic of that name. A gate keeps
          the topic closed until the player holds the flag or has finished the quest; leave both
          blank for something anyone can ask. A word one of this mob's own quests answers to cannot
          be a topic, because the quest is asked first.
        </p>

        {draft.topics.map((topic, index) => {
          const update = (patch: Partial<TopicDraft>) =>
            set({
              topics: draft.topics.map((t, i) => (i === index ? { ...t, ...patch } : t)),
            })
          return (
            <div className="field-row" key={index}>
              <Field label="Keyword" width="md">
                <input
                  value={topic.keyword}
                  placeholder="stone"
                  onChange={(e) => update({ keyword: e.target.value })}
                />
              </Field>
              <Field label="Answer" width="lg">
                <input
                  value={topic.text}
                  placeholder="'Somebody clears the turf round it. Not me.'"
                  onChange={(e) => update({ text: e.target.value })}
                />
              </Field>
              <Field label="Needs flag" width="md">
                <input
                  value={topic.requiresFlag}
                  placeholder="attuned.grask"
                  onChange={(e) => update({ requiresFlag: e.target.value })}
                />
              </Field>
              <Field label="Needs quest done" width="md">
                <input
                  value={topic.requiresQuest}
                  placeholder="a1-1-the-road-out"
                  onChange={(e) => update({ requiresQuest: e.target.value })}
                />
              </Field>
              <Button
                variant="danger"
                onClick={() => set({ topics: draft.topics.filter((_, i) => i !== index) })}
              >
                Remove
              </Button>
            </div>
          )
        })}

        <button
          type="button"
          onClick={() =>
            set({
              topics: [
                ...draft.topics,
                { keyword: '', text: '', requiresFlag: '', requiresQuest: '' },
              ],
            })
          }
        >
          Add topic
        </button>
      </div>

      <label className="field-check">
        <input
          type="checkbox"
          checked={draft.wanders}
          onChange={(e) => set({ wanders: e.target.checked })}
        />
        Wanders between rooms
      </label>

      <p className="dim">
        {draft.wanders
          ? 'This mob moves room to room on its own. A spawner can still override it for a particular placement.'
          : 'Off by default: this mob stays where it is spawned. Leave it off for shopkeepers, quest givers, and anything that should be findable.'}
      </p>

      {/* Only offered once it wanders — "beyond its home zone" is a statement about where the
          wandering may go, and it reads as a live setting on a mob that never moves. */}
      {draft.wanders && (
        <>
          <label className="field-check">
            <input
              type="checkbox"
              checked={draft.roams}
              onChange={(e) => set({ roams: e.target.checked })}
            />
            …and beyond its home zone
          </label>

          <p className="dim">
            {draft.roams
              ? 'This mob will follow exits into neighbouring zones, carrying the stats it spawned with — which came from its own zone’s multipliers.'
              : 'Off by default: this mob wanders freely inside the zone it spawned in and turns back at the border. Rooms flagged noMob still refuse it either way.'}
          </p>
        </>
      )}

      <label className="field-check">
        <input
          type="checkbox"
          checked={draft.shopkeeper}
          onChange={(e) => set({ shopkeeper: e.target.checked })}
        />
        Keeps a shop (players can <code>list</code>, <code>buy</code>, and <code>sell</code> here)
      </label>

      {draft.shopkeeper && (
        <div className="shop-stock">
          <Field
            label="Markup"
            hint="0 is base price; 0.1 is 1.1×."
          >
            <NumberInput
              min={0}
              allowDecimal
              value={draft.markup}
              onChange={(v) => set({ markup: Math.max(0, v) })}
            />
          </Field>

          <p className="dim">
            {draft.markup > 0
              ? `This shop charges ${Math.round((1 + draft.markup) * 100)}% of base value, rounded up to the next whole gold and never less than a gold over — so a 1 gold item costs 2.`
              : 'This shop charges base value. Raise the markup to make it a trader rather than a price list.'}{' '}
            Selling back always pays half of base value, whatever the markup — a shop that is
            expensive to buy from is not automatically one that pays well.
          </p>

          <span className="field-label">Sells</span>
          <p className="dim">
            Stock is unlimited — buying spawns a fresh instance rather than moving one.
          </p>

          {draft.sells.length === 0 && <p className="dim">Nothing stocked yet.</p>}

          {draft.sells.map((itemKey, index) => {
            const template = itemTemplates.find((t) => t.key === itemKey)
            return (
              <div className="field-row" key={`${itemKey}-${index}`}>
                <span className="stock-row">
                  {template ? (
                    <>
                      {template.name}{' '}
                      <span className="dim">
                        {/* The asking price, not the base value: a builder turning the dial should
                            see what it does here rather than by walking into the shop. */}
                        · {shopPrice(template.baseValue, draft.markup)} gold
                        {draft.markup > 0 && ` (base ${template.baseValue})`}
                      </span>
                    </>
                  ) : (
                    /* The template was deleted out from under the shop. Say so here rather than
                       silently dropping the row, which would hide the broken reference. */
                    <span className="bad">{itemKey} — no such item template</span>
                  )}
                </span>
                <Button
                  variant="danger"
                  onClick={() => set({ sells: draft.sells.filter((_, i) => i !== index) })}
                >
                  Remove
                </Button>
              </div>
            )
          })}

          <Field label="Add an item" hint="Type to filter by key or name.">
            <TemplatePicker
              value=""
              options={unstocked.map((t) => ({
                key: t.key,
                name: `${t.name || t.key} (${shopPrice(t.baseValue, draft.markup)} gold)`,
              }))}
              onChange={(key) => {
                // Only a real template is added: this field is an "add" action rather than a
                // value, so a half-typed key must not become a stock row.
                if (unstocked.some((t) => t.key === key)) set({ sells: [...draft.sells, key] })
              }}
            />
          </Field>
        </div>
      )}
    </fieldset>
  )
}

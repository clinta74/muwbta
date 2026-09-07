# Weather, seasons, and what `indoors` is for

> Status: **built**, 2026-09-07, as option B with no mechanical effects at all. Written as a
> proposal earlier the same day; the sections below are kept as the reasoning, with a record of
> what shipped and what deliberately did not immediately after this note. The `indoors` flag had
> been registered since Phase 2 with the summary *"Sheltered from weather, once weather exists"*
> and no reader (BUGS.md #19). It has one now.

## What shipped

The engine tells the time and the weather, and says so when either turns. It changes nothing else.

| Piece | Where |
|---|---|
| The calendar — season, day, hour, daylight | `Muwbta.Domain/Weather/GameInstant.cs` |
| Climate profiles | `Muwbta.Domain/Weather/ClimateProfile.cs` |
| Smooth noise | `Muwbta.Domain/Weather/WeatherNoise.cs` |
| The derived sky | `Muwbta.Domain/Weather/WeatherOracle.cs` |
| Transition and standing lines | `Muwbta.Domain/Weather/WeatherNarration.cs` |
| The turns of the day | `Muwbta.Domain/Weather/DaylightNarration.cs` |
| Narration, `indoors`, the cache | `Muwbta.Engine/Systems/WeatherSystem.cs` |
| The `sky` verb | `Muwbta.Engine/Commands/SkyCommands.cs` |
| The standing line in a `look` | `Muwbta.Engine/Presentation/PlayerView.cs` |

**No mechanical effect anywhere, night included.** Weather is narrated and nothing reads it — not
combat, not regen, not movement, not exits, not spawners, and not `dark`. §6 argued for two early
mechanics that were not balance changes; neither was built, because "just emotes" is a cleaner rule
than "just emotes plus two exceptions", and because either can be added later without revisiting
anything here.

**Narration names nothing that belongs to one world.** No gods, no myths, no places, no proper
nouns, and no suggestion that the weather is anybody's doing — every line is physical and observed:
sky, cloud, light, wind, air, ground. A world with its own account of where the light goes at night
must be free to tell it without contradicting a sentence the engine already printed. Both narration
files say so at the top, because it is the kind of rule that erodes one nicely-written line at a
time.

**Three things from the proposal deliberately did not ship:**

- **Authored climate.** Every world resolves `Temperate` through `Climates.For`. Making `climate` an
  inherited text flag needs `RoomFlagKind` to grow a text kind, and needs both builder flag panels,
  the flag DTO, the `rflag` verb and the bundle validator to stop assuming booleans — a fair change,
  and a disproportionate one on the way to a system with no mechanical effect. The profiles for
  coastal, arid, alpine and subterranean are written and tested; `Climates.For` is the single call
  site that changes.
- **The configurable epoch.** Year zero is a constant in `GameInstant`. Nothing an operator gains by
  moving it is worth a migration, a bundle field and a builder control today; it is one nullable
  timestamp on `game_configurations` when that stops being true.
- **The override table.** Nothing can force a storm yet. It was there to serve authored events, and
  no authored event needs one.

**One content pass is still outstanding**: `indoors` is default-false, so every interior room in the
Reaches currently has weather in it. It wants setting at zone level on the inns, keeps, mines and
anything underground — content work, in the content repo.

---

## Why this needs deciding before it is built

`indoors` is a presentation flag with no behaviour, so nothing is broken today. The moment
weather exists it becomes load-bearing on **every outdoor room in every world**, and it is
default-false — which means the safe value here is "sheltered", and an unflagged world gets no
weather at all rather than getting rain indoors. That is the right default and it is also a
content debt: the flag has to be set, at zone level, on the interiors that already exist,
before anyone will see a sky.

The rest of this is a set of recommendations with the reasoning attached, so the parts that get
overruled get overruled on purpose.

---

## 1. What weather belongs to

**Recommendation: one sky per `World`, biased by a `climate` value that inherits room → zone →
world.**

- **Per-room weather is not weather.** A downpour you walk out of in one step is a room
  description, and rooms already have those.
- **Per-zone weather is nearly as bad.** Zones are small — Ossara is 55 rooms across four of
  them — and a front that stops at a zone boundary is a seam the player can stand on.
- **Global weather is wrong for the Reaches specifically.** It is five `World` rows, and a
  coastal realm and a mountain realm sharing one sky is the thing that would make the feature
  read as a gimmick.

So: the *state* is computed per world. The *character* of that state comes from `climate`.

**`climate` should be a flag, not a column.** `FlagValue` already carries `Text` and `Number`
kinds — they exist so unknown keys survive a round trip (§4.10), and no registry entry uses
them yet. Making `climate` the first `RoomFlagKind.Text` entry buys the whole inheritance chain
for free: a world declares `temperate`, an alpine zone overrides with `alpine`, one cave room
overrides with `subterranean`, and the builder renders it beside the checkboxes with its
inherited source shown, exactly like `pvp`. The alternative — a column on `worlds` — cannot
express the mountain zone without a second column on `zones`, and then a third somewhere for
the room.

This needs `RoomFlags.Resolve` to grow a text-valued sibling and `RoomFlag.Default` to become a
`FlagValue`. That is the only structural change to the flag system this proposal asks for, and
it is one worth making anyway.

Suggested starting climates: `temperate`, `coastal`, `arid`, `alpine`, `subterranean`,
`blighted`. `subterranean` is the interesting one — it is the climate that produces no sky at
all, which is how the Underdark gets weather-free without every room in it needing `indoors`.

---

## 2. The calendar

Weather cannot be seasonal until there is a date, and there is no game clock beyond
`IGameClock.CurrentPulse` today.

**Recommendation: a derived calendar — a pure function of `UtcNow` and a fixed epoch. Nothing
ticks it and nothing stores "what time it is".**

This is the same argument §2.1 makes about the loop: a clock you can only observe is a clock you
cannot test. A derived calendar means a test can ask "what does midwinter look like" without
running the world for a fortnight, and a restart cannot lose or repeat a day.

Proposed scale:

| Unit | Real time | Notes |
|---|---|---|
| 1 game hour | 5 real minutes | |
| 1 game day | 2 real hours | 12 game days per real day |
| 1 month | 28 game days | ≈ 2⅓ real days; also a moon cycle, free |
| 1 season | 3 months = 84 game days | **exactly 7 real days** |
| 1 year | 4 seasons = 336 game days | **exactly 28 real days** |

The two bold rows are why these numbers and not others: **a season is a real week and a year is
a real four weeks.** A returning player is reliably in a different season, and a builder can say
"the festival is in Thaw" and know what that means on a wall calendar. Faster than this and
seasons stop being seasons; slower and most players never see two.

Day length falls out at 2 real hours, so night is roughly 40 real minutes — long enough to be a
thing that happens to you inside one session, short enough not to be a thing you wait out.

**What actually persists:** one `calendar_epoch` (timestamptz) on `game_configurations`, beside
`starting_room_key`. It belongs to the configuration for the same reason the starting room does
— the Reaches' five worlds share a sky's worth of time, and a second world set on the same
server should be able to have its own year zero. Everything else is arithmetic.

Season *names* should be content, not code, for the same reason the welcome message is: a
themed world does not necessarily have Spring. A four-string array on the configuration, with
sensible defaults.

---

## 3. The weather itself, which is the part you asked about

You said "not completely random". There are two honest ways to get that, and they differ in what
the database holds.

### Option A — a persisted Markov walk (the conventional answer)

Each world holds `(state, since, seed)` in a `world_weather` row. On each weather tick, roll the
next state from a season-and-climate-specific transition table and write it back through the
existing save-queue pattern.

- **For:** genuine momentum; trivially tunable by hand ("storms should rarely follow clear");
  the table is a thing a designer can read and edit; it is what every MUD that has done this
  has done.
- **Against:** needs a random source threaded into the loop (§2.1 has one, `SeededRandomSource`,
  but it is deliberately single-threaded and single-purpose); needs a write path, a migration,
  and a save queue; is not reproducible, so "why was it snowing on Tuesday" has no answer; and
  a forecast is impossible by construction.

### Option B — derived weather: a pure function of `(worldKey, climate, gameHour)`

Three smooth scalars per world — **temperature**, **moisture**, **wind** — each built from:

1. a **seasonal baseline**: a sine over the year phase, offset per climate (arid runs hot and
   dry, alpine cold, coastal wet and mild);
2. a **diurnal term** on temperature only (coldest an hour before dawn);
3. **smooth value noise** keyed on `hash(worldKey, seed)`, interpolated over game-hours, at two
   or three octaves — periods of roughly two game days and eight game hours. This is the
   "fronts" term.

Then a small pure classifier maps the triple to one of ~8 states: `Clear`, `Cloudy`, `Overcast`,
`Fog`, `Drizzle`, `Rain`, `Storm`, `Snow` (snow being rain below freezing, blizzard being storm
below freezing).

- **For:** nothing to persist and nothing to lose; survives a restart mid-blizzard for free,
  which is the actual thing "persisted in the database" was asking for; perfectly replayable, so
  a test can assert on midwinter in Ossara; smooth by construction, so no ping-ponging between
  clear and storm; and **you can forecast** — a builder tool can print the next fortnight, which
  is worth a surprising amount when authoring a quest that wants fog.
- **Against:** less hand-authorable; a designer tunes curves rather than a table; and a
  one-off dramatic storm has to be an override rather than a lucky roll.

### Recommendation

**Option B, plus a small override table.** The override is what makes it complete:

```
world_weather_override(world_key, state, from_game_hour, until_game_hour, reason)
```

Written by an admin verb and by quest rewards. Read first; when absent, the derived function
answers. That gets you the authored storm on the night of the siege *and* a sky that never
needs a tick to be correct.

If you would rather have the transition table because it is easier to reason about, take Option
A — but persist `(state, since, nextSeed)` rather than an RNG object, so a restart resumes
rather than rerolls, and keep the classifier's *vocabulary* identical either way so the two are
swappable behind one interface.

Either way the seam is the same and should be built first:

```csharp
public interface IWeatherOracle
{
    WeatherState At(string worldKey, FlagSet? zone, FlagSet? world, GameInstant when);
}
```

Everything below reads that and nothing below cares which option is behind it.

---

## 4. What `indoors` finally does

Four readers, in order of how much they matter:

1. **The room frame.** `RoomPayload` gains a nullable `Weather` string. `PlayerView.SendRoom`
   fills it for outdoor rooms and leaves it null indoors — beside where it already computes
   `dark`, and with the same "asked once for the room" discipline so the frames cannot
   disagree. It must **not** be concatenated into `Description`: that field is authored prose
   and the client styles it as such.
2. **The look prose.** One span after the description, before `Exits`, classed `weather`.
   Suppressed indoors and suppressed in the dark, for the same reason the description is.
3. **Transitions.** When a world's state changes, one line to everyone standing outdoors in it —
   *"The rain eases off."* Not to sleepers (`WorldState.AwakeIn`, which already exists for exactly
   this), and not indoors. This is the only new broadcast and it should be rare: with the noise
   periods above it lands every 20–60 real minutes.
4. **Mechanics.** See §6 — the recommendation is "almost none, at first".

**Indoors should be set at zone level nearly everywhere it is set at all.** An inn, a keep
interior, a mine, the whole Underdark: one flag on the zone. Room-level `indoors` is for the
odd interior room in an outdoor zone. Worth a line in the zone build to-do list (§7.6): a zone
with no `indoors` anywhere is either genuinely all outdoors or has never been thought about.

---

## 5. Where the player asks

There is no `time` verb today, and weather and the date want the same answer:

> *It is a little after dawn on the ninth of Thaw. Rain is falling steadily.*

**Built as one new root verb, `sky`, at priority 2.** `sk` is unclaimed — the `s` prefixes
in use are `say`, `sell`, `sleep`, `stand`, `set`, `spawn`, `stats` — so it costs two keystrokes
and takes nothing from anything. `weather` would work at priority 4 (`wear` holds `wea` at 1, so
`weat` is the shortest unambiguous form) but four characters for a thing typed idly is worse, and
§13's namespace argument applies: root verbs are one shared prefix space and this one should be
cheap.

Indoors, it should say so rather than lying: *"You cannot see the sky from in here."*

---

## 6. Mechanical effects — start with none

The tempting version of this feature changes combat in the rain. **Don't, at first.** Weather
touches every outdoor room in every world simultaneously, so any number attached to it is a
world-wide balance change that arrives on a schedule nobody controls, and §4.4's multipliers are
where difficulty is supposed to live.

Two effects are worth having early because neither is a balance change:

- **Weather as a builder condition.** `RoomExit` already has `RequiredFlagKey` / `RequiredItemKey`
  and an `IsConditional`. A `RequiredWeather` alongside them gives you the mountain pass that
  closes in a blizzard, authored like any other content, with zero systemic risk. Same for
  spawners: the thing that only comes out in fog.
- **Storm plus night plus outdoors reads as `dark`.** Opt-in per zone (a `stormDark` flag, or a
  climate that implies it) — **not** on by default, because switching every outdoor room in the
  world to unviewable for forty minutes at a time would break every zone that has ever been
  written and make lanterns mandatory rather than interesting.

And one that is explicitly **not** recommended for v1: night making outdoor rooms dark on its
own. It is the obvious idea, it is what `dark` looks like it wants, and it would make half of
every player's session a lantern-management minigame in content that was not authored for it.

---

## 7. Suggested order of work

1. `GameInstant` + the calendar arithmetic, with the epoch on `game_configurations`. Pure
   Domain, no wiring. This is testable on its own and everything else depends on it.
2. `RoomFlagKind.Text` and the `climate` registry entry, so the flag chain can carry it.
3. `IWeatherOracle` and the derived implementation. Still pure Domain.
4. `PlayerView` reads it: room frame field, look span, `indoors` respected.
5. The transition broadcast, on a coarse cadence — the 60-second regen tick is already walking
   the world and is fine for this.
6. The `sky` verb.
7. `indoors` set on existing interior zones. **Content work, in the content repo**, and the
   point at which the feature is actually visible.
8. Only then: the override table, exit conditions, spawner conditions.

Steps 1–6 are engine-only and ship without touching a single room. Step 7 is the one that needs
a builder pass over the Reaches.

---

## 8. Open questions

- **Do the five Reaches worlds share a calendar but differ in climate, or should some run their
  own year?** The proposal above assumes shared time, separate skies. A realm that is
  canonically out of step with the others would need the epoch to move from the configuration to
  the world.
- **Should `subterranean` imply `indoors`?** It would save flagging the Underdark twice, but it
  makes one flag quietly set another, which §4.10 has so far avoided.
- **Season names, and whether the year has a number.** Content either way, but a dated world
  wants deciding before quest prose starts referring to it.
- **Does sleep interact with this?** It now requires `peaceful`, which is mostly indoors anyway
  — but "you cannot sleep outdoors in a blizzard" is the kind of rule that sounds good and
  punishes the player who was already somewhere safe. Recommend not.

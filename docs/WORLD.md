# The Reaches

The world this game is set in. **Authored as of 2026-08-15** — the files are in `content/`, and
where they and this document diverged, the files won (§10.4). Everything above the `canon:end`
marker is also the builder assist's canon: `tools/merge-bundles.cs --canon` writes it into the
Reaches' configuration on the way to an import, the builder can edit it there, and
`tools/sync-canon.cs` writes an edit back here. The server embeds nothing of its own.

`PLAN.md` says what the engine does and why. This says what goes in it. Where the two touch, this
document cites the code rather than restating it, because the arithmetic below is only correct for
as long as the arithmetic in `Domain` is.

---

## 1. Setting

### 1.1 What is true

There are **Reaches** — shards of inhabited land — and between them the **Unlit**.

The Unlit is not a gap. It is the binding: what holds the Reaches apart and together at the same
time, and the reason a gate has anywhere to cross *to*. Take it away and the lands do not fall into
each other, they stop having any relation at all.

It is the eldest god and the least personal. The others have names, faces, temples, appetites; it
has none of those and is still the largest thing there is. Priests argue about whether it counts as
a god. Nobody argues about whether it is there.

It is also awake. Standing at a rim and looking out, you can half-see the other Reaches — never in
any detail, never the same twice — and the reason for that indistinctness is that something is in
the way, and it is looking back. People call that **the Regard**. It is not spoken of as an event.
It is spoken of as weather.

### 1.2 What is believed

That **Yrriska** taught the first crossing, and that this was either the salvation of everyone alive
or the worst thing ever done, depending on who is telling it. Both camps leave her the same coin at
the same shrine.

That a gate must **know you** before it will let you through, and that knowing is earned rather than
bought — though a great many people in Grask will sell you a shortcut.

That the Reaches were once nearer to one another. Nothing supports this and everyone repeats it.

### 1.3 What is not true

The Unlit is not evil, does not hunt, and has never been negotiated with. Every story in which it
wants something is a story about the person telling it. This matters for authoring: **the Unlit
never speaks, never acts against an individual, and is never a boss fight.** What players fight in
its domain are things that have been in it too long.

### 1.4 The tonal contract

The register is **frontier and adventurous**. The people are warm, enterprising, superstitious, and
funny. The dark between is patient and is not on their side. That contrast is the whole voice: this
is not a grim world, it is a bright world with something enormous underneath it, and the brightness
is not naive — it is how people who live next to the Unlit stay sane.

| Do | Don't |
|---|---|
| "The rope-ferry runs when Old Ossa feels like walking, which is most days." | "The ferry crossing is treacherous and few return." |
| Let NPCs be busy, competent, and mid-conversation when you arrive | Have NPCs exist to deliver exposition at you |
| Put the dread in the geography and the calm in the people | Put the dread in the people |
| Name prices, tools, and jobs — this is a working frontier | Leave the economy vague and mythic |
| Let a joke stand without undercutting it | Undercut every warm moment with a reminder that all is doomed |

**One rule above the others: the horror is structural, not adjectival.** A room is not frightening
because the prose says it is. It is frightening because the land stops twenty feet ahead and there
is nothing past it, described plainly.

---

## 2. The pantheon

Gods carry the whole content model here. There is **no deity, faith, or faction system in the
engine and this document does not ask for one** — a god is expressed entirely through rooms, prose,
NPCs, mob rosters, loot, and multiplier dials. Where that stops being enough, it is called out as a
future phase rather than assumed (§10.3).

**A zone expresses the god whose domain it is.** This is the load-bearing idea of the whole design,
because it makes the difficulty curve and the theological curve the same curve. A realm's
multipliers stop being arbitrary and become *how much its god cares about you*. The start is
forgiving because Ilvaro is generous. The Unlit is the endgame because it is the only place with no
god over you.

### 2.1 The Wide Gods

Sworn by in every Reach. They are how a level 3 and a level 48 share a religion.

| Name | Domain | Clergy | Register |
|---|---|---|---|
| **Yrriska** | Thresholds, bargains, luck, gates. The trickster who taught the crossing | None formally. Gate-keepers, smugglers, and anyone about to do something stupid | Wry, quick, fond of a wager. Her shrines are the only untidy thing at a gate |
| **Khaldra** | The Kept Fire. Hearth, forge, bread, shelter | Practical, unmystical, usually also the cook | Warm, blunt, competent. Talks about fuel and flour |
| **Verrixa** | Storm and unbound power. Wild magic | Adepts, mostly against their will | Alarming. Speaks in weather metaphors and does not finish sentences |
| **Kheddran** | Oaths and the given word. Binding at human scale | Notaries, judges, marriage-keepers, mercenary captains | Formal, patient, unforgiving of ambiguity |
| **Sevveth** | The dead. Universal because dying is | No temples — only thresholds, and stones with nothing written on them | Quiet. Says less than you want |

**Kheddran and the Unlit rhyme on purpose.** One binds people to one another by their word, the
other binds the Reaches to one another by being there. Act V is where a player is asked whether
those are the same kind of thing (§8.5).

### 2.2 The local gods

One or two per Reach, matched to what that Reach feels like.

**Ilvaro** — *Ossara, bands 1–12.* First journeys, beginner's luck, the road out. Open-handed,
faintly ridiculous, and genuinely beloved; his festivals involve too much food. His clergy are
enthusiastic and under-qualified. **A room in his domain** is green, worked, and safe in a way that
is being actively maintained by someone. **What lives there** is small and does not really want to
fight you. **What it drops** is honest, plain, and slightly better than it needs to be.

**Sulveth** — *Ossara's rim, band 8–12.* Who keeps what is lost. Not a death god — Sevveth handles
dying. Sulveth handles the things and people that simply stop being anywhere, which at a rim is a
recognised category. **A room in her domain** is tidy and unattended, with everything put away by
someone who is not there. **What lives there** was left behind. **What it drops** used to belong to
somebody, and says so.

**Ravvan the Wide Mouth** — *Grask, bands 12–24.* Appetite and enterprise. Not malicious; hungry.
He is why Grask is rich and why nothing in Grask is finished. **A room in his domain** is
half-built, over-supplied, and loud. **What lives there** is competing with you for something.
**What it drops** is valuable and slightly damaged.

**Mhorrek** — *Grask's deep, band 20–24.* What comes due. Ravvan's shadow and, in the older
tellings, his creditor. **A room in his domain** is a place where work stopped abruptly and nobody
came back for the tools. **What lives there** was owed something. **What it drops** is worth less
than it looks — the `gold` dial in his zone is the theology (§4).

**Azhimet** — *Azhen, bands 24–34.* Who measured the Unlit. Its people built the gates and its
clergy went silent — not died, *went silent* — and the instruments they left are still running.
**A room in its domain** is enormous, precise, and built for a purpose you can almost work out.
**What lives there** is either a maintenance system that never stopped or a scholar who did not
leave. **What it drops** is a component of something.

**Nemhalla** — *Nemhal, bands 34–46.* Who is dead. Nemhal fails because she does; the realm's edges
are gone and going, and the reason is lying in `nemhal.keshvaun` where anyone can walk up to it.
**A room in her domain** is a place that is still being maintained by habit. **What lives there**
has not been told. **What it drops** is a relic of a cult that has not noticed.

**The Unlit** — *no domain, because it is the floor under all of them.* In `the-unlit` there is no
local god, and that absence is the point: it is the only place a player stands where nothing is
above them.

---

## 3. The Reaches

Five realms, each a `World` row. `strength` composes as `world × zone` and level moves linearly
with it ([MobLevel.cs](../src/Muwbta.Domain/Inhabitants/MobLevel.cs)), so with a mob chassis
authored at levels 1–10 (§7.1) a realm's world-level `strength` is about **its band ceiling ÷ 10,
less the ~10% the zone dials add back**.

| Realm | Band | `strength` | `xp` | `gold` | `itemValue` | Gods |
|---|---|---|---|---|---|---|
| `ossara` | 1–12 | 1.0 | 1.0 | 1.0 | 1.0 | Ilvaro; Sulveth at the rim |
| `grask` | 12–24 | 1.9 | 1.9 | 2.6 | 1.2 | Ravvan; Mhorrek below |
| `azhen` | 24–34 | 3.0 | 3.0 | 2.1 | 1.0 | Azhimet |
| `nemhal` | 34–46 | 4.1 | 4.1 | 2.5 | 0.8 | Nemhalla, dead |
| `the-unlit` | 46–50 | 4.7 | 4.7 | **0.0** | 0.0 | none |

**`xp` tracks `strength`, and that is forced rather than chosen.** Required XP per level is
`1000·L·(L−1)/2` ([XpProgression.cs](../src/Muwbta.Domain/Characters/XpProgression.cs)), so the XP
needed for one more level grows linearly in `L`. Level also grows linearly in `strength`. Setting
`xp = strength` therefore keeps a kill worth the same fraction of a level everywhere in the game,
which is the only setting that does.

**The other dials are where the gods live.** They are the one place a designer gets to say
something without writing a word of prose:

- **Grask pays too much** (`gold` 2.6 against `strength` 1.9). Ravvan's realm is genuinely rich, and
  a player who goes there early will feel it. That is the trap working as intended.
- **Mhorrek takes it back** — `grask.the-owing` sets `gold` 0.4 against the realm's 2.6, so the
  deepest and hardest zone in Grask is the poorest. Nobody has to explain why.
- **Azhen has no money and very good equipment** (`gold` 2.1). Nobody has lived there for
  generations. What is left is what was built, not what was earned. *The second half of that is now
  said by the item spine alone* — Azhen's set is simply authored better than its band demands —
  since `itemPower`, which used to carry it, applied to nothing and has been deleted (§7.3).
- **Nemhal's economy is dying** (`itemValue` 0.8). Its things are worth less than they were and
  everyone there knows it.
- **The Unlit has no economy at all** (`gold` 0.0, `itemValue` 0.0). A zero multiplier legitimately
  means *none* ([Multipliers.cs](../src/Muwbta.Domain/Worlds/Multipliers.cs) — `xp`, `gold` and
  `itemValue` floor at 0 rather than 1), so coin simply does not drop past the last gate. It is the
  cleanest statement in the design: down here, there is nothing to buy and nobody to buy it from.

### 3.1 The naming tracks the descent

*Ossara* and *Grask* are settler names — people arrived and called the place something. *Azhen* and
*Nemhal* carry their gods' roots, because in those two Reaches the god and the land are one fact:
Azhimet's people were the ones who measured, and Nemhal fails because Nemhalla does. The last realm
has no name at all, only what it is.

**Settler-named → settler-named → god-named → god-named → nameless.** A player who never notices
loses nothing.

The rule runs one level down. Zones in Ossara and Grask take plain common-speech names — Gatetown,
Brackenfell, the Cutting — because settlers named them. Zones in Azhen and Nemhal do not, **with one
deliberate exception each**: `azhen.the-camp` and `nemhal.the-hold` are plainly named because they
are the only things in those Reaches that living people built.

---

## 4. The zones

Sixteen zones. `S` is the composed `world × zone` strength; `min`/`max` are `Zone.MinLevel` and
`Zone.MaxLevel`. **`min` does real work** — effective level is floored at it
([MobLevel.cs:75](../src/Muwbta.Domain/Inhabitants/MobLevel.cs#L75)), which is how a low chassis
mob gets lifted into band instead of becoming worthless filler. This is the exact defect
`PlayTestingNotes.md` records: *"both zones leave every multiplier at 1.0, and both declare
`min_level` 1 — so nothing gets lifted."*

| Zone | Role | God | min–max | zone `str` | S | Other dials | Flags | Rooms |
|---|---|---|---|---|---|---|---|---|
| `ossara.gatetown` | **Start** | Ilvaro | 1–3 | 1.0 | 1.00 | — | `peaceful`, `respawn` | ~16 |
| `ossara.the-terraces` | **Training** | Ilvaro | 1–4 | 1.0 | 1.00 | — | — | ~9 |
| `ossara.brackenfell` | **Grinding** | Ilvaro | 4–10 | 1.0 | 1.00 | — | — | ~14 |
| `ossara.the-rimwalk` | **Story** — Act I | Sulveth | 8–12 | 1.2 | 1.20 | `xp` 1.2, `gold` 0.8 | — | ~11 |
| `grask.the-landing` | **Hub** | Ravvan | 12–14 | 1.0 | 1.90 | — | `peaceful`, `respawn` | ~12 |
| `grask.the-cutting` | **Grinding** | Ravvan | 12–18 | 0.95 | 1.81 | — | — | ~14 |
| `grask.stiltmarsh` | **Grinding** | Ravvan | 16–22 | 1.15 | 2.19 | — | — | ~15 |
| `grask.the-owing` | **Story** — Act II | Mhorrek | 20–24 | 1.26 | 2.39 | `gold` 0.4, `xp` 1.1 | `dark` | ~12 |
| `azhen.the-camp` | **Hub** | — | 24–26 | 1.0 | 3.00 | — | `peaceful`, `respawn` | ~8 |
| `azhen.ummath` | **Grinding** | Azhimet | 24–30 | 1.0 | 3.00 | — | `indoors` | ~14 |
| `azhen.serrivet` | **Grinding** | Azhimet | 28–34 | 1.13 | 3.39 | — | — | ~15 |
| `azhen.thessivar` | **Story** — Act III | Azhimet | 30–34 | 1.15 | 3.45 | — | `indoors`, `dark` | ~13 |
| `nemhal.the-hold` | **Hub** | — | 34–36 | 1.0 | 4.10 | — | `peaceful`, `respawn` | ~8 |
| `nemhal.vurrach` | **Grinding** | Nemhalla | 34–41 | 1.0 | 4.10 | — | — | ~15 |
| `nemhal.olmenneth` | **Grinding** | Nemhalla | 38–45 | 1.1 | 4.51 | — | — | ~15 |
| `nemhal.keshvaun` | **Story** — Act IV | Nemhalla | 42–46 | 1.12 | 4.59 | `xp` 1.1 | `dark` | ~13 |
| `the-unlit.the-crossing` | **Grinding** | none | 46–48 | 1.0 | 4.70 | — | `noRecall` | ~10 |
| `the-unlit.the-regard` | **Story** — Act V | none | 48–50 | 1.06 | 4.98 | — | `noRecall`, `dark` | ~9 |

Every flag named above is in the registry
([RoomFlags.cs](../src/Muwbta.Domain/Worlds/RoomFlags.cs)). **No new flag is required by this
design.** Flags are set at the zone level wherever they apply to the whole zone — that is what
zone flags are for, and it keeps `peaceful` off sixteen individual room rows.

**`noRecall` on both Unlit zones is a real decision.** It means a player who walks in must walk out.
It is the only place in the game that takes `recall` away, and it should be the last thing the
Act IV turn-in warns them about.

### 4.1 Bind points: five, and no more

`respawn` appears on exactly **five zones — the hub of each realm** — and nowhere else. Bind points
are meant to be a handful of deliberate places rather than a property rooms happen to have, and the
flag resolves room → zone → world, so one setting on the hub zone covers every room in it.

That policy does three things at once:

- **Attunement pays twice.** Reaching a new realm gets you access *and* a shorter walk back, because
  its hub is the first place you can bind. Getting to `grask.the-landing` and typing `bind` is a
  real milestone rather than a formality.
- **It makes the conditional-exit exploit impossible by construction** rather than by vigilance
  (`PLAN.md` §4.15). A hub is never behind a locked door, so nobody can bind past a lock and recall
  in without the key. The `/validate` warning becomes a backstop, not the defence.
- **`the-unlit` has no hub and no bind point.** A player who dies there returns to Nemhal's hold, and
  that is the intended shape of the last realm: nothing down there is yours.

An unbound character respawns at `ossara.gatetown` no matter where they died — a punishing walk from
Nemhal, and deliberately so (`PLAN.md` §4.12). It is what makes `bind` worth typing.

**Dying is the way out of the Unlit.** `noRecall` refuses `recall`, but death respawn does not route
through that check, so it always works. The last realm is therefore not a trap: it costs experience
to leave in a hurry, and nothing more. Worth authoring the Act IV warning around, because a player
who believes they are sealed in will behave very differently from one who knows the price.

### 4.2 Effective levels, worked

Chassis levels (§7.1) run through `effective = round(level × S)`, then `max(min, …)`. The floor is
shown in **bold** where it is what put a mob in band.

| Zone | S | Chassis levels used → effective |
|---|---|---|
| `ossara.the-terraces` | 1.00 | 1→1, 2→2, 3→3, 4→4 |
| `ossara.brackenfell` | 1.00 | 4→4, 5→5, 6→6, 7→7, 8→8, 10→10 |
| `ossara.the-rimwalk` | 1.20 | 7→8, 8→10, 9→11, 10→12 |
| `grask.the-cutting` | 1.81 | 5→**12**, 6→**12**, 7→13, 8→14, 9→16, 10→18 |
| `grask.stiltmarsh` | 2.19 | 7→**16**, 8→17, 9→20, 10→22 |
| `grask.the-owing` | 2.39 | 8→**20**, 9→22, 10→24 |
| `azhen.ummath` | 3.00 | 7→**24**, 8→24, 9→27, 10→30 |
| `azhen.serrivet` | 3.39 | 8→**28**, 9→31, 10→34 |
| `azhen.thessivar` | 3.45 | 9→31, 10→35 † |
| `nemhal.vurrach` | 4.10 | 8→**34**, 9→37, 10→41 |
| `nemhal.olmenneth` | 4.51 | 8→**38**, 9→41, 10→45 |
| `nemhal.keshvaun` | 4.59 | 9→**42**, 10→46 |
| `the-unlit.the-crossing` | 4.70 | 9→**46**, 10→47 |
| `the-unlit.the-regard` | 4.98 | 10→50 |

† Deliberately one over the band ceiling. Effective level is **not** clamped at `MaxLevel`, and the
comment saying so calls a boss above its band *"deliberate"* — Azhimet's instrument is the hardest
thing in the Reach and should read that way.

---

## 5. The Rim View

Every Reach ends. Rooms at that edge are **rim rooms**, and they carry a standing convention:

> **The last paragraph of a rim room's description is what you can see of the other Reaches.**
> Never in detail. Never the same twice. Never named — a player who has not been to Grask does not
> learn its name by squinting at it.

This costs nothing but discipline and does three things at once: it makes the world's structure
visible from level 1, it teases every realm the player has not reached, and it is the only place the
Regard is ever *shown* rather than mentioned.

The view degrades as you descend, and that progression is the whole horror arc of the game told in
five paragraphs:

| Realm | What the rim shows |
|---|---|
| `ossara` | Distance, and shapes in it that could be land. Pleasant. People picnic here. The indistinctness reads as haze |
| `grask` | The same shapes, nearer, and one of them has a shape that is not land on it. Nobody in Grask looks for long, and everybody has an opinion about it |
| `azhen` | The instruments here were built to look, and they still are. What they show is not what the eye shows, and the discrepancy is the reason the clergy stopped writing things down |
| `nemhal` | Nothing to see. The rim of Nemhal is where the land has already gone, and what is past it is not distance — it is the absence of the question |
| `the-unlit` | You are the view now. Whatever a person on an Ossaran rim is squinting at, on a clear afternoon, is you |

That last row is the payoff, and it is worth authoring the other four carefully so it lands.

---

## 6. Progression

### 6.1 Bands interlock, and the taper is why

A character earns nothing from a mob below `level / 2`
([XpRelevance.cs](../src/Muwbta.Domain/Characters/XpRelevance.cs)) and full value from a mob at or
above their own level, on a straight line between. So the design constraint is not "cover every
level" — it is **every level must have something at or above it that is reachable.**

Sorted, the effective levels §4.2 produces are:

```
1 2 3 4 5 6 7 8 10 11 12 13 14 16 17 18 20 22 24 27 28 30 31 34 35 37 38 41 42 45 46 47 50
```

**Every level from 1 to 50 has a full-value target inside its own realm** — not merely somewhere in
the game, which would be no use to a player who has not attuned yet. Checked level by level against
`XpRelevance.Fraction`, the worst payout available to a character anywhere in their own realm's band
is 1.000. Nothing here reproduces the level-6 cliff `PlayTestingNotes.md` reports, and the reason it
does not is `MinLevel`: every band's floor lifts a low chassis into it, which is precisely the dial
those two zones left at 1.

The pinch is at a realm's ceiling, and it is deliberate. A level 25 who has not yet attuned to Azhen
is fighting Grask's best at 0.93 of value — enough to keep moving, little enough to make the next
gate attractive. That is what an attunement chain is for, and it is why each act sits at the top of
its realm's band rather than the middle.

### 6.2 Attunement is the storyline

A gate is inert until it knows you, and coming to be known is a quest chain. This means
**progression gating and the storyline are the same content**, which is worth stating plainly
because it halves the work: there is no separate "unlock" system to design, and no story that runs
alongside progression rather than being it.

The engine's quest shape is exactly one thing — talk to a giver, fetch an item, deliver it to a
turn-in — chained by prerequisites (`PLAN.md` §4.9). Every act below is that, four to six times.

| Act | Zone | Ends by attuning to |
|---|---|---|
| I | `ossara.the-rimwalk` | `grask` |
| II | `grask.the-owing` | `azhen` |
| III | `azhen.thessivar` | `nemhal` |
| IV | `nemhal.keshvaun` | `the-unlit` |
| V | `the-unlit.the-regard` | — |

### 6.3 Gating is geography first, and a flag second

**Geography does the coarse work.** The gate to realm N+1 sits deep inside realm N's storyline zone,
so reaching it at all means surviving realm N. That costs nothing to author and it is a real design
choice rather than a workaround: a player who fights their way to a gate they have not earned should
*find* it, and find that it does not open. That is a better scene than a closed door.

**The gate itself is a conditional exit**, specified in `PLAN.md` §4.15 — an exit that requires a
character flag, an inventory item, or both. Attunement is a **character flag granted as a quest
reward**, which is what makes the last quest of each act mean something mechanically rather than
only narratively.

Three consequences for this document:

- **Attunement is per character, never per account.** An account-level flag would hand a fresh alt
  the last realm at level 1. Every act is earned again on every character, which is the intended
  shape of a 1–50 game.
- **The gate names the flag, not the quest.** Acts can be re-authored, split, or given a second
  route without touching a single exit — which matters because §8.5 is deliberately unwritten.
- **The refusal line is authored per exit.** Every gate in the game says *"The gate does not know
  you."* because someone wrote that on it, not because the engine has an opinion.

Locked doors inside a zone are the same mechanism with an item instead of a flag, which is where
Azhen's components and Nemhal's cult keys earn their place.

---

## 7. Rosters

### 7.1 The chassis

Mob templates are **global** (`PLAN.md` §4.8), which is what makes the multiplier design pay off.
Ten chassis templates carry every ordinary fight in the game, re-skinned by realm through nothing
but dials.

| Key | Level | Role |
|---|---|---|
| `chassis-vermin` | 1 | Trash. One attack, no loot worth naming |
| `chassis-scavenger` | 2 | Trash, flies, `wanders` |
| `chassis-pack-hound` | 3 | Comes with friends — spawner `targetCount` 3+ |
| `chassis-drudge` | 4 | Humanoid labourer. First mob that fights back properly |
| `chassis-cutpurse` | 5 | Light humanoid, fast attacks, steals nothing (no mechanic for it) |
| `chassis-bruiser` | 6 | Beast. Slow, heavy, teaches interrupting |
| `chassis-brigand` | 7 | The standard humanoid. Most common mob in the game |
| `chassis-warden-beast` | 8 | Beast elite. Carries an `EffectKey` attack — `control.root` |
| `chassis-revenant` | 9 | Undead elite. Carries `control.stun` |
| `chassis-warleader` | 10 | Boss chassis. Multi-attack, always a named re-skin |

**A chassis is placed under a plain kind name, and the spawner adds the zone's word.** A
template is "a brigand"; the spawner that places it carries a one-word `nameModifier`, and the
mob appears as "a hill brigand" in Brackenfell and "a marsh brigand" in Stiltmarsh from the same
row. So a kind is **one template wherever the arithmetic allows**, re-skinned by placement rather
than by authoring a second row that differs in one word. Where the fiction or the numbers
genuinely differ by realm — Azhimet's bronze, the kept who carry quest supply, Grask's trash at
chassis 5–6 because experience follows the template's base value and not the floored level — the
realm keeps its own row under the same kind name. Named characters and bosses never take a
modifier. The roster and its keys are settled in STORY.md §3.2.

### 7.2 Named mobs

Per realm, on top of the re-skins: two or three shopkeepers, one to three quest givers per act, and
one named boss per storyline zone. Quest givers and shopkeepers set `wanders: false` on the
template — *"a quest giver that wanders off is a chain nobody can finish."*

### 7.3 The item spine

Eight slots exist (`ItemSlot`: Head, Chest, Hands, Legs, Feet, MainHand, OffHand, Trinket). The
spine is **one full set per realm**, five sets, plus per-act quest rewards.

**There is no item-power dial, so the mob trick does not transfer.**
[ItemSpawner.cs](../src/Muwbta.Engine/Spawning/ItemSpawner.cs) copies `BaseStats` verbatim and
resolves only `ItemValue`, into the price. **Each realm's set is authored at its own final
numbers.**

`itemPower` used to sit in the §3 table recording where the good equipment was meant to be — Azhen
1.3, the Unlit 1.4 — while putting it nowhere. This document already called that *"a dial to either
implement or delete, not a blocker"* and judged deleting it the smaller lie; the milestone review
carried that out (BUGS.md #17). `spawnDensity` went with it for the same reason, which is why the
zone table's dial column is emptier than it was.

**Armour is two authored numbers and one decision per realm.** A piece carries `armor`, which decides
what a landed blow costs, and optionally `defense`, which decides how often one lands (`PLAN.md`
§4.6). The realm decision is the *set total*, because mitigation is `A / (A + 100)` capped at 75%:

| Realm | Set total `armor` | Mitigation | Set total `defense` |
|---|---|---|---|
| `ossara` | 25 | 20% | 1 |
| `grask` | 55 | 35% | 2 |
| `azhen` | 95 | 49% | 3 |
| `nemhal` | 150 | 60% | 4 |
| `the-unlit` | 210 | 68% | 5 |

Split the total across the eight slots by bulk — chest heaviest, gloves and boots lightest, the
shield carrying most of the `defense`. The exact split is taste; the total is the balance decision,
and `ArmorCurveTests` pins these five rows so the spine and this table cannot drift apart.

Keep `defense` small and mostly on shields. It is added to a d20 target, so five points is a quarter
of the whole die — the curve bounds `armor` for you, and nothing bounds `defense` but restraint.

Weapons carry `damageMultiplier` and optionally `bonus`. That multiplier scales the unarmed dice, so
unlike the retired armour multipliers it works on its own.

**A `Trinket` counts as armour now.** It was absent from `IsArmorSlot` and is not one of the two
hands a damage multiplier is read from, so the eighth slot equipped and did nothing at all. It is
part of the set total above.

Quest items set `isQuestItem: true`, which means only *cannot be sold or destroyed* (`PLAN.md`
§4.9). They still drop from ordinary spawners and loot tables; there is no separate pipeline.

**Four zones are `dark`, and two items answer them.** The Owing, Thessivar, Keshvaun and the Regard
withhold everything but their exits from anyone standing in them without a light (`PLAN.md` §4.18) —
which is a whole act each in Grask, Azhen, Nemhal and the Unlit. Both answers are bought rather than
found, on purpose: a light that drops is a light one player in five has.

| Item | Where | Slot | Cost |
|---|---|---|---|
| a pitch torch | Gatetown's trader and provisioner, and Grask's provisioner | `OffHand` | 6 |
| a hooded pit lamp | Grask's outfitter | `Trinket` | 48 |

The torch is level-1 cheap and available three hundred rooms before it is needed, which is the
point: nobody is gated on gold. What it costs is the off hand — no shield, no second weapon — and
the pit lamp is what you buy to get that hand back, at eight times the price and in the realm whose
own act is the first dark one. **One lit item lights the room for everyone in it**, so the honest
group answer is one lamp between six.

`nemhal-unlit-lamp` is deliberately not a light. It is a vigil lamp *"never lit, kept filled"*, and
the joke only works if it stays dark.

### 7.4 Shops

A shopkeeper is a mob template with `shopkeeper` in its `Behavior` bag and a `sells` list.

| Where | What |
|---|---|
| `ossara.gatetown` | Four: general, weapons, armour, provisioner. Low markup — Ilvaro's town is generous |
| `grask.the-landing` | Three, all of them selling more than they should and marking it up. Ravvan |
| `azhen.the-camp` | One trader, thin stock, buying more eagerly than selling |
| `nemhal.the-hold` | One quartermaster. Rationing, not trading |
| `the-unlit` | **None.** `gold` is 0.0 and there is nobody down here |

---

## 8. The storyline

Five acts. Every quest below is the engine's one shape: one giver, one required item and count, one
turn-in, prerequisites, four dialogue strings.

### 8.1 Act I — Ossara, the Rimwalk (levels 8–12)

The gate at the rim has always been there and has always worked. This season it does not. The chain
is a village solving a practical problem — a broken road, essentially — and the last quest is the
first time anyone says the word *Regard* out loud.

**Beat:** a chain of small errands for Ilvaro's under-qualified clergy ends with Sulveth's
keeper, who has been at the rim the whole time, and who explains that a gate does not break — it
forgets. Attunement to `grask`.

### 8.2 Act II — Grask, the Owing (levels 20–24)

Grask is rich and everyone is behind on something. The chain is hired work for a company that is
plainly not telling you what is at the bottom of its deepest cut, and the discovery is that the
crew who opened Grask's gate two generations ago paid for it with something, and the payment is
still being collected.

**Beat:** the `gold` 0.4 dial in this zone is the story. A player will notice the richest realm's
deepest hole pays nothing before any NPC tells them why. Attunement to `azhen`.

### 8.3 Act III — Azhen, Thessivar (levels 30–34)

Azhimet's people built the gates. The instrument at Thessivar was built to watch the Unlit and has
not stopped. The chain is archaeology — recovering components, restarting a reading — and the
reading is the first hard information in the game: **the Regard is not attention, it is
recognition.** It is not watching the Reaches. It is watching for something specific, and Azhimet's
clergy went silent because they worked out what.

**Beat:** the player restores an instrument that then tells them something nobody wanted. Attunement
to `nemhal`.

### 8.4 Act IV — Nemhal, Keshvaun (levels 42–46)

Nemhalla is dead and her Reach is going with her. The chain runs through a cult that has not been
told, maintaining a body out of habit, and ends at the body itself. Nemhalla did not die of
anything. She **stood in the way**, once, on purpose, and it cost her everything, and it worked.

**Beat:** the only place in the game a god can be walked up to and touched. The last turn-in warns
the player, in plain language, that past the next gate `recall` does not work. Attunement to
`the-unlit`.

### 8.5 Act V — the Unlit, the Regard (levels 48–50)

No god above you, no coin, no way home but walking. The chain resolves what the Regard is looking
for, and closes on the question Kheddran's whole domain has been rhyming against since level 1:
whether a binding that holds everything together is owed anything by the things it holds.

**The ending is a choice and not a fight.** The Unlit is never a boss (§1.3). What the player fights
in the last zone are things that have been in the binding too long, and what they *do* at the end is
answer.

**This act is deliberately under-specified here.** It should be designed last, once the four acts
beneath it exist and their tone is known, rather than written now and then constrained by it.

---

## 9. Naming and key conventions

### 9.1 Gods

True names take two or three syllables with stress on the first; doubled consonants and mid-word
clusters (`rr ss vv kk dh zh kh`); no apostrophes and no hyphens; `y` as a vowel marks the wild and
the tricky (*Yrriska*). **Epithets are plain common speech** — "the Kept Fire", "the Wide Mouth",
"who keeps what is lost". That split is also why **the Unlit** and **the Regard** are plain: naming
is a personal act, and it is the one thing too large to have a true name.

### 9.2 Keys

`RoomKey` is exactly three dot-separated segments of `[a-z0-9-]`, no leading or trailing hyphen,
128 characters maximum ([RoomKey.cs](../src/Muwbta.Domain/Worlds/RoomKey.cs)). A zone key must
begin with its world key plus a dot, which the engine enforces
([WorldMutationApplier.cs:192](../src/Muwbta.Engine/Mutations/WorldMutationApplier.cs#L192)).

| Kind | Convention | Example |
|---|---|---|
| World | One short segment. Short because it is typed into every room key | `ossara` |
| Zone | `world.name`, articles kept where the name has one | `ossara.the-rimwalk` |
| Room | Descriptive, not numbered | `ossara.the-rimwalk.the-last-marker` |
| Mob chassis | `chassis-*` | `chassis-brigand` |
| Mob re-skin | `<realm>-<name>` | `grask-claimjumper` |
| Item | `<realm>-<name>`, or bare for cross-realm staples | `azhen-lens-housing`, `bread` |
| Quest | `<act><n>-<slug>` so a chain sorts | `a1-3-the-last-marker` |

### 9.3 Prose

Room titles are noun phrases in title case, no trailing punctuation. Descriptions are two to four
short paragraphs; the rim-room convention (§5) adds one more. Second person, present tense. **The
room describes what is there, not how to feel about it.**

---

<!-- canon:end -->
<!--
  Everything above this line is embedded in the server and sent to the builder assist as its
  standing context (src/Muwbta.Server/Assist/Canon.cs). Everything below is authoring process -
  true, useful, and not part of what the world is. A test fails if the canon above grows past the
  budget the model's context window allows, so if that test starts failing, the question is which
  section has stopped being canon rather than how to raise the number.
-->

## 10. Authoring notes

### 10.1 How content lands

Content is authored as **v6 `WorldBundle` JSON** checked into the repository and applied through
`POST /api/builder/import` ([WorldBundle.cs](../src/Muwbta.Server/Building/WorldBundle.cs)). The
bundle carries worlds, zones, rooms with nested exits, item templates, mob templates, abilities,
spawners, and quests — everything this document specifies and nothing player-owned.

Three properties of that path worth knowing before authoring against it:

- **`FormatVersion` must be exactly 6.** A version mismatch is the one hard refusal in the whole
  import path, deliberately, and it moves whenever the shape does — 5 was the spawner level pin, 6
  is conditional exits. Author against whatever `WorldBundle.CurrentFormatVersion` says rather than
  against this sentence.
- **A spawner carries its own `Id`.** That is what makes re-importing idempotent — a bundle that
  minted fresh ids would double every zone's population on the second run. Author the GUIDs once and
  keep them.
- **Import is a merge, not a mirror.** There is no replace or delete mode
  ([WorldImporter.cs](../src/Muwbta.Server/Building/WorldImporter.cs)), so removing something from
  a bundle does not remove it from the world. Deletions are explicit API calls.

**The world is authored.** As of 2026-08-15 all eighteen zones exist in `content/`: 224 rooms,
67 mob templates, 70 items, 90 spawners, and fifteen quests across five acts. What this document
specifies and what the files contain agree, and where they diverged the files won — the divergences
are recorded in §10.4.

**Room terrain: done, and generated rather than drawn.** All 224 rooms carry a 21×9 grid — the
layout service's own default size, so a room with terrain and one without sit the same size beside
each other. Each **zone declares a terrain kind** (or several, picked per room key) and the art is
drawn from a RNG seeded with the room key, which is what makes regeneration byte-identical: a random
seed would rewrite every room on every run and no diff could be read.

Twenty-one kinds cover the Reaches — `field`, `scrub`, `marsh`, `pier`, `hall`, `ruin`, `cave`,
`street`, `rim`, `standing` and the rest. Two of them are load-bearing rather than decorative:

- **`rim` is chosen by the prose, not declared.** A room carrying Rim View text (§5) already says
  the land stops there, so that is the signal — the convention reaches the map without anything new
  being written per room.
- **`standing` is the Unlit.** A floor with void all round it and nothing underneath, which is the
  one piece of terrain here making a point instead of decorating one.

**Solid tiles are named from the engine's list.** `RoomLayoutService.NonPlaceableTiles` decides what
a mob may be drawn standing on and matches on the *legend name*, so calling a pillar "column" would
silently put a rat inside it. The Reaches added five names to that set — `void`, `pillar`, `rock`,
`crate`, `brazier` — and `tools/check-bundle.cs` reads the set off `RoomLayoutService.NonPlaceable` rather than
transcribing it, then refuses any room whose grid is ragged, whose legend misses a character it
draws, or which leaves under 40 cells to stand on. That last one matters: entities are placed only
on open ground and are simply *not drawn* when there is none, so an all-water room is a room whose
occupants vanish.

Hand-painting individual set-piece rooms through the builder's `GridPainter` is still worth doing
and is now an edit rather than a blank page.

### 10.2 Retiring Aldenmoor — decided: leave it where it is

**Aldenmoor is not deleted.** It is its own `World`, and reachability in this engine is the exit
graph and nothing else: movement follows `RoomExit` rows, and the only key-addressed teleport is
`goto`, which requires the Builder role. A world that nothing links to is sealed by not being linked
to — the same mechanism that makes portals work, unused.

Three doors are worth having checked rather than assumed, and all three shut on one config change:

| Door | Where it leads | Why it shuts |
|---|---|---|
| A new character's first room | `options.StartingRoom` | Moves to `ossara.gatetown` |
| `recall`, bound or not | bind point, else `StartingRoom` ([TravelCommands.cs:44](../src/Muwbta.Engine/Commands/TravelCommands.cs#L44)) | Nobody is bound in Aldenmoor — production has no characters |
| Death | `RespawnRoomKey ?? StartingRoom` ([CombatSystem.cs:1142](../src/Muwbta.Engine/Systems/CombatSystem.cs#L1142)) | Same |

So the cost is smaller than this section used to claim, in three ways:

- **Production never seeds Aldenmoor at all.** Seeding is gated behind `IsDevelopment()`
  ([Program.cs:297](../src/Muwbta.Server/Program.cs#L297)) — *"starter content, which is a fixture,
  not schema."* After the migration squash there is nothing on the production server to delete.
- **Moving the starting room is configuration, not code.** `Engine__StartingRoom` is read at
  [Program.cs:65](../src/Muwbta.Server/Program.cs#L65) and overrides the default. The constant in
  `EngineContracts.cs` stays as it is and remains correct for development and tests.
- **The four playtest plans keep working**, and are better for it: a retired Millbrook stops changing,
  which makes it a steadier regression fixture than a zone under active balance work.

Keeping it also leaves a builder sandbox that is not authored content — somewhere to `goto` and test
engine behaviour without spawning test mobs into a zone players will see.

**One thing genuinely must change**, and it is unrelated to whether Aldenmoor stays:
[GameLoop.cs:489](../src/Muwbta.Engine/GameLoop.cs#L489) greets every player on every login with a
hardcoded `"Welcome to Aldenmoor"`, whichever world they are standing in. Tracked in
`PlayTestingNotes.md`, to be done when the Gatetown rooms exist to point at.

### 10.4 Where authoring overruled the design

Three things changed on contact with the arithmetic, and the tables above have been left as they
were so the difference is visible rather than tidied away.

- **Deep realms re-skin the top of the chassis table, not all of it.** §7.1 reads as though every
  realm re-skins all ten rows. It cannot: with `S` near 3, a level 2 chassis lands well under the
  zone floor and is lifted to exactly `minLevel`, so every mob in the zone fights at the same
  number and the band has no shape. Azhen, Nemhal and the Unlit use chassis 7–10; Grask uses 5–10;
  only Ossara uses the whole table. §4.2's worked rows already said this and the roster section did
  not.
- **`the-unlit.the-crossing` is flat at 46.** Chassis 7, 8 and 9 all floor to it at `S` 4.70, and
  only a level 10 would clear it. It is in band, it is a ten-room transitional zone, and a Reach
  where everything has been in the binding equally long reads better uniform than graded — but it
  is flat by arithmetic rather than by choice, and worth knowing before anyone tunes it.
- **Act V is written.** §8.5 deliberately under-specified it. It is authored now, and it holds to
  what that section asked for: nothing in the last zone is a boss fight against the Unlit, the
  final beat is an answer rather than a kill, and the one thing standing between the player and
  the niche is the oldest person who ever walked in on purpose.
- **The roster is kinds, not re-skins.** Sixty-eight templates became fifty-nine in the story
  pass of 2026-09-04: nine global kinds (`rat` to `lurcher`, and `bruiser`) placed by spawners
  that carry a zone word, realm rows only where the fiction or the arithmetic needs them (Azhimet's
  bronze; Grask's trash at chassis 5–6, because experience follows the base value and not the
  floored level), and the kept as one kind in five realms. STORY.md §3.2 is the record; §7.1
  above was rewritten to match, and the "fifty rows" this section used to promise never happened.

### 10.3 What this design asks of the engine, and what it leaves alone

**One engine dependency, and it is built.** Conditional exits — `PLAN.md` §4.15 — are what turn
attunement from a story beat into a gate: an exit may require a character flag, an inventory item,
or both, and a quest grants the flag. Attunement chains therefore need no further engine work.
They took `WorldBundle.FormatVersion` to **6**.

Four things beyond that are deliberately left as *later* rather than assumed:

- **A faith or faction system.** Gods here are content. If standing, favour, or god-granted
  abilities are ever wanted, that is a phase of its own and it interacts with the Path system.
- **`POST /api/builder/zones/{key}/respawn`** is documented in `PLAN.md` §7.3 and **not mapped** in
  `BuilderEndpoints`. Until it is, editing a zone's multipliers only affects future spawns and
  tuning a zone means restarting the server. This will be felt immediately when the tables in §3 and
  §4 meet reality.
- ~~**`itemPower` is snapshotted and never applied.**~~ **Deleted**, along with `spawnDensity`, in
  the milestone review — both were authored, editable and applied by nothing (BUGS.md #17). The
  design never depended on either: every realm's set is authored at final numbers, and that is now
  the only way it is said.
- **Rarity is authored now, and mostly unused.** The spawner's `respawnSeconds` was in the same dead
  state and came back built rather than deleted, because deleting it surfaced the question it had
  been hiding: *how is one thing rarer than another?* Default 60 seconds; one replacement per
  window, so clearing a room of four buys four windows (`PLAN.md` §4.8).

  **The five act bosses are at 600 seconds** — `ossara-the-unclaimed`, `grask-the-creditor`,
  `azhen-the-last-reader`, `nemhal-the-first-mourner`, `unlit-the-first-held`. Ten minutes rather
  than the hour first proposed, and the reason is worth keeping: **each one drops its act's gate item
  at chance 1.0**, so its respawn is a progression gate. At an hour, a wipe costs an hour and two
  players who both need the item queue. A boss that should genuinely be hourly wants loot that gates
  nothing — which is a content change, not a dial.

  **Nothing in `content/` is a rare ground spawn yet.** All five item spawners are act quest supply
  (`ossara-fallen-marker` and its siblings) and sit at the default deliberately: a four-hour marker
  would make Act I unfinishable. The capability is there for the first genuinely rare item somebody
  authors.
- **`Trinket` is not wired to anything.** It is absent from `IsArmorSlot` and is not one of the two
  hands the damage multiplier is read from, so the eighth slot equips and does nothing. Worth one
  of: adding it to the armour sweep, giving it its own stat vocabulary, or dropping it from the
  spine. Until then, trinkets are flavour and sale value.

# The Reaches

The world this game is set in. **Authored as of 2026-08-15** — the files are in `content/`, and
where they and this document diverged, the files won (§10.3).

Everything above the `canon:end` marker is the builder assist's canon, and is about the world and
how to write it, not about the engine that runs it. `tools/merge-bundles.cs --canon` writes it into
the Reaches' configuration on the way to an import, the builder can edit it there, and
`tools/sync-canon.cs` writes an edit back here. `PLAN.md` says what the engine does; nothing above
the marker needs to.

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

Gods carry the whole world here. There is no faith or faction to join: a god is expressed entirely
through rooms, prose, NPCs, what lives in a place and what it leaves behind.

**A zone expresses the god whose domain it is.** This is the load-bearing idea. The start is
forgiving because Ilvaro is generous; the Unlit is the end because it is the only place with no god
over you. How hard a Reach is and how much its god cares about you are the same fact.

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

**Ilvaro** — *Ossara, levels 1–12.* First journeys, beginner's luck, the road out. Open-handed,
faintly ridiculous, and genuinely beloved; his festivals involve too much food. His clergy are
enthusiastic and under-qualified. **A room in his domain** is green, worked, and safe in a way that
is being actively maintained by someone. **What lives there** is small and does not really want to
fight you. **What it drops** is honest, plain, and slightly better than it needs to be.

**Sulveth** — *Ossara's rim, levels 8–12.* Who keeps what is lost. Not a death god — Sevveth handles
dying. Sulveth handles the things and people that simply stop being anywhere, which at a rim is a
recognised category. **A room in her domain** is tidy and unattended, with everything put away by
someone who is not there. **What lives there** was left behind. **What it drops** used to belong to
somebody, and says so.

**Ravvan the Wide Mouth** — *Grask, levels 12–24.* Appetite and enterprise. Not malicious; hungry.
He is why Grask is rich and why nothing in Grask is finished. **A room in his domain** is
half-built, over-supplied, and loud. **What lives there** is competing with you for something.
**What it drops** is valuable and slightly damaged.

**Mhorrek** — *Grask's deep, levels 20–24.* What comes due. Ravvan's shadow and, in the older
tellings, his creditor. **A room in his domain** is a place where work stopped abruptly and nobody
came back for the tools. **What lives there** was owed something. **What it drops** is worth less
than it looks, and the deepest hole in the richest Reach paying nothing is the whole of his
theology.

**Azhimet** — *Azhen, levels 24–34.* Who measured the Unlit. Its people built the gates and its
clergy went silent — not died, *went silent* — and the instruments they left are still running.
**A room in its domain** is enormous, precise, and built for a purpose you can almost work out.
**What lives there** is either a maintenance system that never stopped or a scholar who did not
leave. **What it drops** is a component of something.

**Nemhalla** — *Nemhal, levels 34–46.* Who is dead. Nemhal fails because she does; the realm's edges
are gone and going, and the reason is lying in Keshvaun where anyone can walk up to it.
**A room in her domain** is a place that is still being maintained by habit. **What lives there**
has not been told. **What it drops** is a relic of a cult that has not noticed.

**The Unlit** — *no domain, because it is the floor under all of them.* In the last realm there is
no local god, and that absence is the point: it is the only place a player stands where nothing is
above them.

---

## 3. The Reaches

Five realms, walked in this order. Each is harder than the last because its god is further from
caring about you.

| Realm | Levels | Gods | What it is |
|---|---|---|---|
| `ossara` | 1–12 | Ilvaro; Sulveth at the rim | The green Reach, and the one everybody starts in. Worked land, short roads, and a gate in the middle of town that has not opened in living memory |
| `grask` | 12–24 | Ravvan; Mhorrek below | A shard opened two generations ago and still being emptied. Rich, loud, and unfinished |
| `azhen` | 24–34 | Azhimet | Opened, measured, abandoned. Its people built the gates and left their instruments running |
| `nemhal` | 34–46 | Nemhalla, dead | The failing Reach. Its edges are gone and going |
| `the-unlit` | 46–50 | none | Not a Reach across the binding. Inside it |

**The economies are theology, and a player feels them before anyone explains.**

- **Grask is genuinely rich**, richer than it should be, and a player who goes there early will feel
  it. That is the trap working as intended.
- **Mhorrek takes it back.** The Owing is the deepest and hardest place in Grask and the poorest.
  Nobody has to explain why.
- **Azhen has no money and very good equipment.** Nobody has lived there for generations. What is
  left is what was built, not what was earned.
- **Nemhal's economy is dying.** Its things are worth less than they were and everyone there knows
  it.
- **The Unlit has no economy at all.** Coin does not drop past the last gate, there is nothing to
  buy and nobody to buy it from. It is the cleanest statement in the design.

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

Eighteen. Each realm opens at a hub, grinds through two or three, and ends in the zone its act is
told in.

| Zone | Role | God | Levels | What it is |
|---|---|---|---|---|
| `ossara.gatetown` | Start | Ilvaro | 1–3 | The town around the oldest known gate, which nobody living has seen work |
| `ossara.the-terraces` | Training | Ilvaro | 1–4 | Farm shelves stepping down the hill, where everyone learns which end of a billhook is which |
| `ossara.brackenfell` | Grinding | Ilvaro | 4–10 | Scrub woodland and old quarry. Firewood, charcoal, and a toll nobody agreed to |
| `ossara.the-rimwalk` | **Act I** | Sulveth | 8–12 | Where the land stops. Sulveth's ground, and the road out of Ossara |
| `grask.the-landing` | Hub | Ravvan | 12–14 | Where everything arriving in Grask arrives, and most of it is sold twice |
| `grask.the-cutting` | Grinding | Ravvan | 12–18 | The first claim, worked out and worked again |
| `grask.stiltmarsh` | Grinding | Ravvan | 16–22 | The wet ground east of the cut, built over rather than drained |
| `grask.the-owing` | **Act II** | Mhorrek | 20–24 | Ravvan's deepest cut and Mhorrek's ledger. Work stopped here abruptly. Dark |
| `azhen.the-camp` | Hub | — | 24–26 | Nine years of survey tents pitched inside somebody else's hall |
| `azhen.ummath` | Grinding | Azhimet | 24–30 | The near halls. Enormous, precise, and built for a purpose you can almost work out |
| `azhen.serrivet` | Grinding | Azhimet | 28–34 | The instrument yards, open to the sky and still turning |
| `azhen.thessivar` | **Act III** | Azhimet | 30–34 | The great instrument, built to watch the Unlit, and never switched off. Dark |
| `nemhal.the-hold` | Hub | — | 34–36 | Eleven people and a wall, holding a line against an edge that is not attacking |
| `nemhal.vurrach` | Grinding | Nemhalla | 34–41 | The near country, still maintained, by habit, by people nobody has relieved |
| `nemhal.olmenneth` | Grinding | Nemhalla | 38–45 | The processional road and the houses along it, all of them still keeping the office |
| `nemhal.keshvaun` | **Act IV** | Nemhalla | 42–46 | Where Nemhalla is lying. The only place in the Reaches a god can be walked up to. Dark |
| `the-unlit.the-crossing` | Grinding | none | 46–48 | Inside the binding. Nothing here is yours and nothing here is for sale |
| `the-unlit.the-regard` | **Act V** | none | 48–50 | Where the binding is looking from. Dark |

**Four zones are dark**, one per realm from Grask down, and a player standing in one without a light
sees nothing but the way out. Both answers are bought rather than found: a pitch torch for six coin,
which costs you the off hand, and a hooded pit lamp for forty-eight, which is what you buy to get
that hand back. One lit item lights the room for everyone in it.

**The Unlit is the only place a player must walk out of.** Nothing recalls from down there. It is
not a trap — dying still returns you to Nemhal's hold — but it costs, and the Act IV turn-in warns
about it in plain language. Nothing in the last realm is yours, including the way home.

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

## 6. The gates

A gate is inert until it knows you, and coming to be known is a quest chain. **Progression and the
storyline are therefore the same content**: there is no separate unlock to design, and no story
running alongside progression rather than being it.

| Act | Zone | Ends by attuning to |
|---|---|---|
| I | `ossara.the-rimwalk` | `grask` |
| II | `grask.the-owing` | `azhen` |
| III | `azhen.thessivar` | `nemhal` |
| IV | `nemhal.keshvaun` | `the-unlit` |
| V | `the-unlit.the-regard` | — |

**Geography does the coarse work.** The gate to the next realm sits deep inside this one's storyline
zone, so reaching it at all means surviving the realm. A player who fights their way to a gate they
have not earned should *find* it, and find that it does not open. That is a better scene than a
closed door, and what it says is always the same sentence: **"The gate does not know you."**

Being known is earned again on every character. Locked doors inside a zone are the same idea with a
key instead, which is where Azhen's components and Nemhal's cult keys earn their place.

---

## 7. Rosters

### 7.1 Kinds, not one-off names

Ordinary creatures are named by **kind** — a rat, a crow, a hound, a thief, a cutpurse, a boar, a
brigand, a bruiser, a lurcher — and the place they are put adds one word: a barn rat in the
Terraces, a hill brigand in Brackenfell, a marsh brigand in Stiltmarsh, a wharf rat on the Landing.
The same kind runs the length of the game, so a player learns what a lurcher does once and knows it
in five Reaches.

Where a realm's fiction genuinely differs it keeps its own row under the same kind name: Azhimet's
bronze is a servitor, an engine, a warden; Nemhal has bearers; the Unlit has drifters. Named people
and bosses never take a place-word — they have names.

**The kept are one idea in five realms and the best one in the roster.** Everything left standing at
a rim is Sulveth's: a kept traveller in Ossara, a kept miner in Grask, a kept scholar in Azhen, a
kept priest in Nemhal, a kept crosser in the Unlit. They are not undead and they are not ghosts.
They stopped being anywhere, and they are still holding what they were holding.

The full roster and its keys are settled in `STORY.md` §3.2.

### 7.2 Named mobs

Per realm: two or three shopkeepers, one to three quest givers per act, and one named boss in the
storyline zone. A quest giver or shopkeeper never wanders — a giver who walks off is a chain nobody
can finish.

Non-quest NPCs can be **asked about things**, and that is where most of the world's lore lives: a
keyword, an answer, and sometimes a gate on it so the same person says more once the player has got
further. `STORY.md` §3.4 has the pass that exists; new NPCs should carry two or three topics each.

### 7.3 The item spine

One full set of equipment per realm — head, chest, hands, legs, feet, main hand, off hand, trinket —
plus what each act hands out. Five sets, each better than the last, each written in its realm's
voice: Ossara's is honest leather and hobnails, Grask's is plated and bought at a markup, Azhen's is
bronze and better than it has any business being, Nemhal's is vigil gear from a cult that is
rationing, and the Unlit's is what people carried in and did not carry out.

Quest items cannot be sold or destroyed, and are otherwise ordinary — they drop from the same
creatures everything else does.

`nemhal-unlit-lamp` is deliberately not a light. It is a vigil lamp, *"never lit, kept filled"*, and
the joke only works if it stays dark.

### 7.4 Shops

| Where | What |
|---|---|
| `ossara.gatetown` | Four: general, weapons, armour, provisioner. Low markup — Ilvaro's town is generous |
| `grask.the-landing` | Three, all of them selling more than they should and marking it up. Ravvan |
| `azhen.the-camp` | One trader, thin stock, buying more eagerly than selling |
| `nemhal.the-hold` | One quartermaster. Rationing, not trading |
| `the-unlit` | **None.** There is no coin down here and nobody to take it |

---

## 8. The storyline

Five acts. Every quest is one shape: a giver asks for a thing, the player fetches it, someone takes
it. The story is what they say while it happens.

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

**Beat:** the money is the story. A player notices that the richest realm's deepest hole pays
nothing long before any NPC tells them why. Attunement to `azhen`.

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
the player, in plain language, that past the next gate there is no way home but walking. Attunement
to `the-unlit`.

### 8.5 Act V — the Unlit, the Regard (levels 48–50)

No god above you, no coin, no way home but walking. The chain resolves what the Regard is looking
for, and closes on the question Kheddran's whole domain has been rhyming against since level 1:
whether a binding that holds everything together is owed anything by the things it holds.

**The ending is a choice and not a fight.** The Unlit is never a boss (§1.3). What the player fights
in the last zone are things that have been in the binding too long, and what they *do* at the end is
answer.

**Beat:** the oldest person who ever walked in on purpose is sitting on a marker stone, entirely
calm, and the first thing she tells you is that you can go back. Most people do. She would like that
written down somewhere.

---

## 9. Naming and key conventions

### 9.1 Gods

True names take two or three syllables with stress on the first; doubled consonants and mid-word
clusters (`rr ss vv kk dh zh kh`); no apostrophes and no hyphens; `y` as a vowel marks the wild and
the tricky (*Yrriska*). **Epithets are plain common speech** — "the Kept Fire", "the Wide Mouth",
"who keeps what is lost". That split is also why **the Unlit** and **the Regard** are plain: naming
is a personal act, and it is the one thing too large to have a true name.

### 9.2 Keys

A room key is exactly three dot-separated segments of lowercase letters, digits and inner hyphens,
and a zone key must begin with its world key and a dot.

| Kind | Convention | Example |
|---|---|---|
| World | One short segment. Short because it is typed into every room key | `ossara` |
| Zone | `world.name`, articles kept where the name has one | `ossara.the-rimwalk` |
| Room | Descriptive, not numbered | `ossara.the-rimwalk.the-last-marker` |
| Creature kind | Bare where it is used everywhere, `<realm>-<kind>` where a realm keeps its own | `brigand`, `azhen-warden` |
| Named person or boss | `<realm>-<name>` | `ossara-rim-keeper`, `grask-the-creditor` |
| Item | `<realm>-<name>`, or bare for cross-realm staples | `azhen-lens-housing`, `bread` |
| Quest | `<act><n>-<slug>` so a chain sorts | `a1-3-the-last-marker` |

### 9.3 Prose

Room titles are noun phrases in title case, no trailing punctuation. Descriptions are two to four
short paragraphs; the rim-room convention (§5) adds one more. Second person, present tense. **The
room describes what is there, not how to feel about it.**

Names carry their own article — "a rat", "a pitch torch" — and named people take a clause rather
than a title: "Corun, who keeps the fire", "Keeper Adda of Sulveth's stone".

---

<!-- canon:end -->
<!--
  Everything above this line is the builder assist's canon, written into the Reaches' configuration
  by tools/merge-bundles.cs --canon. Everything below is authoring process - true, useful, and not
  part of what the world is. A test fails if the canon above grows past the budget the model's
  context window allows, so if that test starts failing, the question is which section has stopped
  being canon rather than how to raise the number.
-->

## 10. Authoring notes

### 10.1 How content lands

Content is authored as **v6 `WorldBundle` JSON** checked into the repository and applied through
`POST /api/builder/import` ([WorldBundle.cs](../src/Muwbta.Server/Building/WorldBundle.cs)). The
bundle carries worlds, zones, rooms with nested exits, item templates, mob templates, abilities,
spawners, quests and configurations — everything this document specifies and nothing player-owned.

Three properties of that path worth knowing before authoring against it:

- **`FormatVersion` must match the build.** A version mismatch is the one hard refusal in the whole
  import path, deliberately, and it moves whenever the shape does. Author against whatever
  `WorldBundle.CurrentFormatVersion` says rather than against this sentence.
- **A spawner carries its own `Id`.** That is what makes re-importing idempotent — a bundle that
  minted fresh ids would double every zone's population on the second run. Author the GUIDs once and
  keep them.
- **Import is a merge, not a mirror.** There is no replace or delete mode
  ([WorldImporter.cs](../src/Muwbta.Server/Building/WorldImporter.cs)), so removing something from
  a bundle does not remove it from the world. Deletions are explicit API calls.

**The canon travels with the content.** The configuration row carries what the assist reads, and
`merge-bundles --canon docs/WORLD.md --into the-reaches` writes everything above the marker into it
on the way to an import. `tools/sync-canon.cs` is the return leg, from a panel edit back into this
file. The server embeds no canon of its own: a configuration that arrives without one tells the
assist there is no world description rather than handing it somebody else's.

**A configuration tags the worlds it is for**, so `?configuration=the-reaches` exports the five
realms, the templates they need, the abilities, and the configuration with its canon — the unit the
story is authored in, and the one that should travel.

**The world is authored.** All eighteen zones exist in `content/`: 238 rooms, 59 mob templates,
93 items, 100 spawners, and thirty-five quests across five acts. What this document specifies and
what the files contain agree, and where they diverged the files won — the divergences are recorded
in §10.3.

**Room terrain: done, and generated rather than drawn.** Every room carries a 21×9 grid — the
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
`crate`, `brazier` — and `tools/check-bundle.cs` reads the set off `RoomLayoutService.NonPlaceable`
rather than transcribing it, then refuses any room whose grid is ragged, whose legend misses a
character it draws, or which leaves under 40 cells to stand on. That last one matters: entities are
placed only on open ground and are simply *not drawn* when there is none, so an all-water room is a
room whose occupants vanish.

### 10.2 The dials, and where they live

The realm and zone multipliers this document used to tabulate are content, not design: they are in
`content/`, they are editable in the builder, and the arithmetic that consumes them is in
`Muwbta.Domain`. What matters and is not obvious from either:

- **`xp` tracks `strength`.** Required experience per level grows linearly, and level grows linearly
  with strength, so setting the two equal keeps a kill worth the same fraction of a level everywhere
  in the game. It is the only setting that does.
- **A zone's `min_level` does real work.** Effective level is floored at it, which is how a
  low-level creature gets lifted into band instead of becoming worthless filler. Two zones that left
  every multiplier at 1.0 and declared `min_level` 1 are the defect `PlayTestingNotes.md` records.
- **Experience follows the template's authored value, not the floored level.** A level-1 rat lifted
  to 12 by a zone floor still pays a level-1 reward, which is why Grask keeps its own trash rows at
  levels 5–6 rather than placing the global ones.
- **A spawner can pin a level and add a name word**, so one template can be a hill brigand at 7 and
  a marsh brigand at 20 without a second row.

### 10.3 Where authoring overruled the design

Changed on contact with the arithmetic, and recorded rather than tidied away.

- **Deep realms use the top of the level range, not all of it.** With a realm's strength near 3, a
  low-level creature lands under the zone floor and is lifted to exactly `min_level`, so everything
  in the zone fights at the same number and the band has no shape. Azhen, Nemhal and the Unlit use
  levels 7–10; Grask uses 5–10; only Ossara uses the whole range.
- **`the-unlit.the-crossing` is flat at 46.** Levels 7, 8 and 9 all floor to it, and only a 10 would
  clear it. It is in band, it is a ten-room transitional zone, and a Reach where everything has been
  in the binding equally long reads better uniform than graded — but it is flat by arithmetic rather
  than by choice, and worth knowing before anyone tunes it.
- **Act V is written.** It was deliberately left under-specified until the four acts beneath it
  existed. It holds to what that asked for: nothing in the last zone is a boss fight against the
  Unlit, the final beat is an answer rather than a kill, and the one thing between the player and
  the niche is the oldest person who ever walked in on purpose.
- **The roster is kinds, not re-skins.** Sixty-eight templates became fifty-nine in the story pass
  of 2026-09-04: nine kinds placed everywhere by spawners that carry a zone word, realm rows only
  where the fiction or the arithmetic needs them, and the kept as one idea in five realms.
  `STORY.md` §3.2 is the record.

### 10.4 What this design asks of the engine, and what it leaves alone

Everything this document specifies is built. Three things are deliberately left as *later* rather
than assumed:

- **A faith or faction system.** Gods here are content. If standing, favour, or god-granted
  abilities are ever wanted, that is a phase of its own and it interacts with the Path system.
- **`POST /api/builder/zones/{key}/respawn`** is documented in `PLAN.md` §7.3 and **not mapped** in
  `BuilderEndpoints`. Until it is, editing a zone's multipliers only affects future spawns and
  tuning a zone means restarting the server.
- **Rarity is authored and mostly unused.** A spawner's `respawnSeconds` defaults to 60, with one
  replacement per window, so clearing a room of four buys four windows. **The five act bosses are at
  600 seconds** — ten minutes rather than the hour first proposed, because **each one drops its
  act's gate item at chance 1.0**, so its respawn is a progression gate. At an hour, a wipe costs an
  hour and two players who both need the item queue. A boss that should genuinely be hourly wants
  loot that gates nothing, which is a content change rather than a dial.

  **Nothing in `content/` is a rare ground spawn yet.** The item spawners are all act quest supply
  and sit at the default deliberately: a four-hour marker would make Act I unfinishable.

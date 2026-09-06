# The Reaches — story guide

**Draft for review, 2026-09-04.** A companion to [WORLD.md](WORLD.md). That document is canon and is
embedded in the server for the builder assist, under a token budget (`CanonTests`). This one is
not embedded and is not budgeted: it is where the plot, the cast, and the plan for deepening both
live, so WORLD.md can stay a rulebook.

Three parts. **§1–2** describe the story exactly as `content/` tells it today, and where it is
thin. **§3** proposes what to add — mortal through-lines, a race roster, zone modifiers, NPC talk.
**§4** is the engine work those proposals need, sized against the code. §3.1 and §3.2 were settled
in review on 2026-09-04; §3.3–3.4 follow from them. Nothing in §4 is built yet.

---

## 1. The story as authored

### 1.1 The divine layer, by plot role

WORLD.md §2 gives each god a domain and a register. This table gives each one a *job in the story*,
which is the thing to check when adding content: a god with no job is a god nobody will ever meet.

| God | Job in the plot | Voice in `content/` today |
|---|---|---|
| **The Unlit** | The binding. Awake. Not watching *at* the Reaches but watching *for* something, and has never had to search. The ending reveals what: it is watching for somebody to notice it. | Rooms only. Never speaks (by rule). |
| **Yrriska** | Taught the first crossing. Every gate has her shrine, every crew touches it. The Waiting One in the Regard — who "walked in on purpose" and set the first marker stone — is written so she *might* be Yrriska, and is never confirmed. | Shrines in four gate rooms; Hesper sweeps around one. Nobody talks about her. |
| **Khaldra** | Hearth and bread. Corun's nineteen-year fire. Vesh's embers "do not go out and do not warm anything". | Corun, Khaldra's Hearth. No plot role. |
| **Verrixa** | Storm, wild magic. | **None.** The Adept's stormrod is the only trace. |
| **Kheddran** | The given word. The ending's question — whether a binding that holds everything together is owed anything — is *his* question, asked in his terms. | **None before Act V.** Named once, in the last turn-in. |
| **Sevveth** | The dead. Whether Nemhalla is *dead* or merely *lost* is the argument that got Aveth expelled, and it is a Sevveth-versus-Sulveth argument that nobody frames that way. | **None.** |
| **Ilvaro** | The road out. Beginner's luck. Pell's whole chain is Ilvaro's clergy solving a broken road. | Pell, the deacon. Ilvaro's House. |
| **Sulveth** | Keeps what is lost. Her domain is the game's "undead": everything left standing at a rim, in every realm, is hers. | Adda, the rim keeper. The left behind. |
| **Ravvan** | Appetite. Why Grask is rich and unfinished. | The Landing's three shops; "everything in Grask has been eating well". |
| **Mhorrek** | What comes due. "Not a punishment, an invoice." | The Creditor, the owed, the ledger room. |
| **Azhimet** | Measured the Unlit, built every gate, then went silent on purpose. | Vane, Immeth, the instruments, the one occupied seat. |
| **Nemhalla** | Stood in the gap and is still standing in it. Nemhal fails at exactly her rate. | The lying place. Aveth. The cult that nurses her. |

### 1.2 The spine: five facts, one per act

The storyline is a chain of *revelations*, not events. Nothing happens in the Reaches during the
game; the player learns what has already happened, one fact per gate.

| Act | What the player learns | Who says it |
|---|---|---|
| I | **A gate does not break. It forgets.** Somebody stopped saying the name of the other side. | Adda, at the Keeper's Stone |
| II | **Grask's gate was opened on credit**, two generations ago, and the first company is still paying weekly. Azhen built the gates and set the terms. | Roan, at the Owing |
| III | **The Regard is recognition, not attention.** It watches *for* one thing, has never lost it, and Azhimet's last reader scraped the plate and stayed in the seat rather than say what. | Vane, at the Camp |
| IV | **Nemhalla stood in the way, on purpose, and it worked.** She is the edge of Nemhal. The cult is not mourning her; it is nursing her. | Aveth, at the Hold |
| V | **The Unlit binds by being there**, unasked and unthanked, and it has been watching for somebody to notice. The answer is a position, handed back. Most people go back. | The Waiting One, at the niche |

**One object runs through all five acts: gate-stone.** The shard in Ossara's last marker, the raw
seam in Grask's first workings ("the piece they cut them from"), Azhen's silent room lined with it,
the lying place cut from the largest single piece anyone has found, and the face itself in the
Regard. It is the only physical thing the player touches in every realm, and no NPC ever remarks
on the fact. That is either a deliberate quiet or an opportunity, and §3.1 treats it as the latter.

### 1.3 Act by act

| Act | Zone | Giver → turn-in | Boss (drops the gate item at 1.0) | Levels |
|---|---|---|---|---|
| I | `ossara.the-rimwalk` | Pell → Pell → Adda → Adda | the Unclaimed | 8–12 |
| II | `grask.the-owing` | Vance → Roan → Roan | the Creditor | 20–24 |
| III | `azhen.thessivar` | Vane throughout | the Last Reader | 30–34 |
| IV | `nemhal.keshvaun` | Aveth throughout | the First Mourner | 42–46 |
| V | `the-unlit.the-regard` | the Waiting One throughout | "the oldest thing still holding on" | 48–50 |

Every act is three quests of the engine's one shape (talk, fetch, deliver), and the third grants
the attunement flag. The *last* quest's turn-in prose is where each act's fact lands. That makes
fifteen paragraphs of dialogue the entire load-bearing story, which is the first finding in §2.

**Act I — The Road Out.** Pell has noticed the gate stopped when the marker line came down and has
"not said it is the reason, only that nobody has checked". The player re-stands markers, then
collects keepsakes from the left behind, and Adda re-orders the events: things went first, stones
second, gate third. The shard from the Unclaimed is warm and "was never one piece". Adda: "Go and
see what is left of Grask, and mind what looks back."

**Act II — What Was Paid.** Vance hires you for tags, is honest about the pay, and changes the
subject about who is wearing them. The tags are first-company names. Roan sends you for ledger
leaves: wages, paid weekly, the last entry this week. The Creditor wears the first foreman's coat
and the book is full. "They opened the gate on credit. Mhorrek is not a punishment, he is an
invoice. Azhen built the gates. Go and ask them what the terms were."

**Act III — What It Is Watching For.** Vane needs parts from the yards because the camp's catalogue
has none. Cogs seat with a click after four hundred years. The plates for the last century are
blank — scraped from the inside, carefully, "and people do not carefully destroy a thing they think
is wrong". The reading is a *position*, identical on plates ninety years apart. The Last Reader is
still at the eyepiece. "Nemhal is closer to the edge than we are. Go and ask them what it looks
like from there."

**Act IV — She Stood In The Way.** Aveth said out loud that Nemhalla was dead and was put out for
it. Tokens: two hundred years of issue, still climbing, so the untold are being *replaced*. The
office: every prayer is for a recovery. Past the bier the chamber narrows to a face of gate-stone
that is the outside, and she is lying in the gap she is filling. Aveth makes you say the recall
warning back to her before you go.

**Act V — What Is Owed.** "You can go back. Say it back to me." Eight things people were still
holding; forty sets of gear on the standing ground against a much larger count of arrivals — most
people go back, and she wants that written down. Five worn markers, the oldest of which she set,
meaning to walk to the face and back. The First Held has the answer because nobody came for it.
She puts it in the niche and sits down. "It has been watching for somebody to notice. That is the
whole of it."

### 1.4 The cast

Everyone with a name, what they know, and which thread (§3.1) they could carry.

| Name | Where | Role | What they know or hold | Thread |
|---|---|---|---|---|
| Hesper | Gatetown, gate yard | Shrine sweeper | Sweeps around Yrriska's bowl and never touches it. Hates the pigeons. | Marker Road |
| Old Ossa | Gatetown, ferry | Ferryman | Whether the Reach is named for him. | colour |
| Corun | Gatetown, hearth | Innkeeper | Nineteen years, not once out. | The Fire |
| Deacon Pell | Gatetown | Act I giver | Fourth deacon in six years. Has the order of events backwards. | — |
| Sesk | the Terraces | Warden | Which season each wall repair is from. | colour |
| Tessa Roke, Berrin Halt, Wick, Maddy Thorn | Gatetown | Shops | Roke's ledger has three names marked "no longer needs it". | — |
| Keeper Adda | the Rimwalk | Act I turn-in | Thirty years at the blank stone. "A gate forgets." Somebody clears the Watch Stone's turf with a blade, recently — not her. | Marker Road, the Kept |
| Reddick Vance | the Landing | Act II giver | Eleven years hiring; never seen a first-company tag come back up. | The Account |
| Ollam Vech | the Landing | Quaymaster | "Everything that comes through here is written down." Visibly a week from collapsing. | The Account |
| Ista Roan | the Owing | Act II turn-in | More trips down than anyone alive. "Do not touch the rope in the sump." | The Account |
| Hask, Bessa Krail, Tam Oduls | the Landing | Shops | Krail sells a *debt token* as a trinket. | — |
| Survey-lead Immeth | the Camp | Non-quest | Nine years, a third through. No room in Azhen is not square. | The Watchers |
| Bellic Vane | the Camp | Act III | The only living person who can work an instrument, "and he set that bar". | The Watchers |
| Ordrel Sask | the Camp | Trader | Buys every cog, does not ask. | — |
| Captain Orrun | the Hold | Non-quest | Eleven people. "We have moved the wall twice." | — |
| Quartermaster Dell | the Hold | Shop | Rations, not trades. | — |
| Sister Aveth | the Hold | Act IV | Expelled for saying "dead". Nine years within forty feet of the body, never went down. | The Kept, Sevveth |
| Vesh | every story zone | Epic chain | At every crossing in every story, no older in any of them. Works for materials. Keeps a jar of embers and will not say where the first came from. | The Fire |
| The Waiting One | the Regard | Act V | Set the first marker. "Not from any Reach you can place." | Marker Road |

Bosses are not cast; they are the last kept thing in each realm: the Unclaimed (made of more than
one person's belongings), the Creditor (the first foreman's coat), the Last Reader, the First
Mourner (never relieved), and the one at the bottom of the binding.

### 1.5 The side chain

Vesh runs a parallel five-step chain per Path (`e1`–`e5`, four Paths, twenty quests), gated on each
act's finish. Step one wants three embers from the Unclaimed; every later step wants the previous
piece back "when it has been through something". Her last line: "There is nothing past here to
make it out of." She is the only mortal who crosses every gate, and she says nothing about the
story at all — which §3.1 E treats as a secret rather than an omission.

---

## 2. Where it is thin

Findings from reading all of it in one sitting. Numbered so §3 can point at them.

1. **Fifteen paragraphs carry the whole plot.** The story is in the act turn-ins and nowhere a
   player can go back and ask. Rooms reinforce it well (the milestone re-cut in a smaller figure,
   the vigil book's three words, the taut rope), but rooms cannot be questioned.
2. **Non-quest NPCs are colour, not story.** Six of twenty named NPCs have `greeting` lines; the
   lines are good and none of them advance anything. `talk <npc> <words>` only does one thing today:
   accept a quest. Immeth and Orrun, the two people written as knowing the most, have no quest and
   so nothing to say beyond two greetings.
3. **Nobody has heard of anybody in the next realm.** Adda sends you to Grask; nobody in Grask
   knows Adda exists. Vane sends you to Nemhal; Aveth has never heard of Thessivar. Only Vesh moves,
   and she does not talk.
4. **Three Wide Gods have no voice.** Verrixa is absent. Sevveth is absent from a realm whose whole
   act is about whether a god is dead. Kheddran carries the ending's question and is named once,
   at the end, by someone the player has known for three quests.
5. **Two naming systems collide.** Named mortals are fine (Tessa Roke, Ollam Vech, Old Ossa). Fodder
   and bosses use *descriptive phrases as names*: "one of the left behind", "one who stayed", "a
   housing hauler", "a clamp arm", "a floor sweeper", "a shroud binder", "a drift", "the oldest
   thing still holding on". Ten templates use the "one of the…" pronoun form, which needed its own
   branch in `NarrationHelper` to stop reading "an one of the owed" forty times a fight. They are
   evocative once and wearing by the tenth kill.
6. **Nothing repeats, so nothing is recognised.** 68 templates for 18 zones, every one a hand
   re-skin. `hill toller`, `claimjumper`, `spoil picker`, `company bruiser` are the same chassis-7
   human with different words, and a player has no way to know that a "lurcher" in Ossara and a
   "stilt lurker" in Grask are the same animal. The chassis design (WORLD.md §7.1) was meant to
   make the descent legible; the naming hides it.
7. **The kept are five names for one thing.** Left behind → owed → one who stayed → untold →
   long held / recognised. It is the best idea in the roster — Sulveth's domain as the game's
   undead, in every realm — and it is invisible because each realm calls them something else.
8. **The best material is at level 48.** "You can go back" and the first-marker beat are the
   strongest writing in the game and reachable by nobody for a long time. Some of the Marker Road
   can be seeded at level 8 without spending it (§3.1 A).
9. **Unplaced.** `azhen-dust-mite`, `nemhal-crypt-mite`, `golden-goose`. The first two are
   chassis-1 vermin in realms that use chassis 7–10 only (WORLD.md §10.3), so they can never be in
   band; the goose is a fixture. Delete the mites or give them a pin.

Not a finding: the quest text has no encoding damage. The "�" seen in a console dump was the
console; the files carry proper em-dashes.

---

## 3. Proposals

### 3.1 Mortal through-lines — settled

**Settled 2026-09-04.** All five threads stand as written; Verrixa stays absent.

Five threads, each owned by a god who currently has no plot, each told by non-quest NPCs across at
least three realms, each *paying* in the act it belongs to rather than adding a sixth. The rule:
**a thread is seeded by talk, confirmed by a room, and paid by a quest that already exists.**

**A. The Marker Road — Yrriska.** Ossara's marker line is cut with "a mark that is not any letter
Gatetown uses". The Watch Stone is older than the markers, uncut, and somebody clears its turf with
a blade. The stones continue inside the binding at a hundred paces. The Waiting One set the first.
*Seed:* Hesper on why she sweeps around the bowl ("she was untidy, they say, and she got across");
Adda on who clears the Watch Stone ("not me, and not anybody I have seen"); the gate crew at the
Landing on "stepping off". *Confirm:* the Second Marker in the Crossing. *Pay:* `a5-2`. Whether
the Waiting One is Yrriska stays unsaid. **Cost: talk only.**

**B. The Account — Kheddran.** Every realm keeps a book. Vance's tags are "issued against a name";
Vech writes everything down; the ledger room is still being entered; the Creditor's book is full;
Azhen's registry ends in one line that is not a reading; the vigil book is begun and not finished
every day; the Waiting One wants "most people go back" written down somewhere. That is Kheddran's
domain — a promise is what is written — and the ending asks whether the binding, which keeps every
Reach's word without one of its own, is owed one. *Seed:* Vech is Kheddran's man (a notary who
logs the gate is exactly his clergy); Vance answers "the pay is the pay" with the oath he swore.
*Confirm:* the Standing Account. *Pay:* `a5-3`, whose turn-in already names him. **Cost: talk, plus
one new line on Vech.**

**C. The Watchers — Azhimet.** Azhen was in every Reach before the settlers. The sump mountings in
the Cutting were built by "whoever knew what they were doing and did not work for the company".
Nemhal's milestone is cut in "a measure nobody uses now". Every gate is theirs. *Seed:* Roan on
the pump mountings; Immeth on the measure ("if you find a room in Azhen that is not square"); Dell
on the milestone. *Confirm:* the first workings ("Azhen cut every gate in the Reaches. This is the
piece they cut them from"). *Pay:* `a2-3` and `a3-3`. **Cost: talk only.**

**D. The Kept — Sulveth and Sevveth.** One race in five realms (§3.2). Adda and Aveth are the same
person twice: a keeper outside her institution, thirty years and nine, both certain of a fact the
institution cannot say. Aveth's expulsion is a theological argument — *dead* (Sevveth) versus
*lost* (Sulveth) — and Nemhalla is the only case where both are wrong. *Seed:* Adda on the
difference ("she keeps what is lost, she does not give it back — two facts"); Aveth on which god
she prayed to when she said the word. *Pay:* `a4-3`. **Cost: talk, plus the roster.**

**E. The Fire — Khaldra.** Vesh's embers are "not warm and have not gone out". Corun's fire has not
been out in nineteen years. If Vesh's first ember came from a hearth, Khaldra's Kept Fire is the
one thing that has crossed every gate with the player, in a jar. *Seed:* Corun, on a woman who
took a coal from his fire the first winter and paid in a knife he still has. *Pay:* `e5`, where
Vesh says there is nothing past here to make it out of. **Cost: one greeting and one emote.**

Verrixa is left without a thread on purpose. Wild magic has no place in a story that is about
binding, and a god who is deliberately absent from the Reaches is more interesting than one wedged
in. Worth one line from an Adept-facing NPC, no more.

### 3.2 The roster — settled

**Settled 2026-09-04.** Fourteen kinds, thirty-two fodder and boss templates in place of forty-four,
and every one of them named the same way: an article, an optional zone word from the spawner, a
plain noun. The twenty named mortals are unchanged.

Two things moved on contact with the arithmetic:

- **Grask keeps its own trash.** A mob's experience is its template's base value times the zone's
  `xp` dial — not its effective level — so a level-1 rat that the Cutting's floor lifts to 12 pays
  level-1 experience for a level-12 fight. Grask's rat, crow and hound therefore stay realm
  templates at chassis 5–6, as they are today, and only the kinds that clear Grask's floor on
  their own (cutpurse and up) are global.
- **Merging is the only reason a key changes.** The rule is still *rename names freely, keep
  keys*, but two templates becoming one need one key. Spawners are rewritten in the content pass
  regardless, and no quest, loot table or shop list references a fodder key, so a new key costs
  nothing. Every old key is listed against its replacement below.

`azhen-dust-mite` and `nemhal-crypt-mite` are deleted; `golden-goose` stays a fixture.

**Global · bare keys, Ossara-band chassis**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Rat | `rat` | 1 | `r` | a rat | terraces: barn | ossara-barn-rat |  |
| Crow | `crow` | 2 | `c` | a crow | terraces: terrace | ossara-terrace-crow | wanders |
| Hound | `hound` | 3 | `d` | a hound | terraces, brackenfell: scrub | ossara-scrub-dog | pack, targetCount 3+ |
| Thief | `thief` | 4 | `h` | a thief | terraces, brackenfell: hedge | ossara-hedge-thief |  |
| Cutpurse | `cutpurse` | 5 | `p` | a cutpurse | brackenfell: road · the-cutting: claim | ossara-road-cutpurse, grask-spoil-picker |  |
| Boar | `boar` | 6 | `B` | a boar | brackenfell: bracken · stiltmarsh: marsh | ossara-bracken-boar, grask-marsh-hog |  |
| Brigand | `brigand` | 7 | `t` | a brigand | brackenfell: hill · the-rimwalk: rim · the-cutting: claim · stiltmarsh: marsh | ossara-hill-toller, grask-claimjumper | most common mob in the game |
| Bruiser | `bruiser` | 8 | `t` | a bruiser | the-cutting, stiltmarsh, the-owing: company | grask-company-bruiser |  |
| Lurcher | `lurcher` | 8 | `L` | a lurcher | brackenfell, the-rimwalk: gorse · the-cutting, stiltmarsh: stilt · the-owing: deep | ossara-gorse-lurcher, grask-stilt-lurker | control.root |

**Grask trash · per realm at chassis 5–6**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Rat | `grask-rat` | 5 | `r` | a rat | the-cutting: wharf | grask-wharf-rat |  |
| Crow | `grask-crow` | 5 | `c` | a crow | stiltmarsh: pier | grask-gullrat | wanders; the old key named a gull |
| Hound | `grask-hound` | 6 | `d` | a hound | the-cutting: dock | grask-dock-cur | pack |

**Azhen · Azhimet's bronze**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Flitter | `azhen-flitter` | 7 | `c` | a brass flitter | ummath, serrivet: none | azhen-brass-flitter | wanders; name kept |
| Servitor | `azhen-servitor` | 8 | `p` | a servitor | ummath: hall · serrivet: yard | azhen-sweeper, azhen-tally-servitor, azhen-lens-grinder | drops drive cogs |
| Engine | `azhen-engine` | 9 | `B` | an engine | ummath: hall · serrivet: yard · thessivar: tower | azhen-clamp-arm, azhen-housing-hauler | drops drive cogs |
| Warden | `azhen-warden` | 9 | `t` | a warden | ummath: hall · serrivet: yard · thessivar: tower | azhen-instrument-warden | control.stun |

**Nemhal · the office**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Moth | `nemhal-moth` | 7 | `c` | a vigil moth | vurrach: none | nemhal-vigil-moth | wanders; name kept |
| Hound | `nemhal-hound` | 8 | `d` | a hound | vurrach: hollow | nemhal-hollow-hound | pack |
| Lay brother | `nemhal-lay-brother` | 8 | `h` | a lay brother | vurrach, olmenneth: none | nemhal-lay-brother | name kept |
| Bell-ringer | `nemhal-bell-ringer` | 8 | `p` | a bell-ringer | vurrach: none | nemhal-bell-ringer | name kept |
| Bearer | `nemhal-bearer` | 9 | `B` | a bearer | vurrach: road · olmenneth: station · keshvaun: vigil | nemhal-bier-bearer, nemhal-shroud-binder |  |
| Warden | `nemhal-warden` | 9 | `t` | a warden | vurrach: road · olmenneth: station · keshvaun: vigil | nemhal-vigil-keeper | control.stun; drops vigil tokens |

**The Unlit**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Drifter | `unlit-drifter` | 7 | `d` | a drifter | the-crossing: none | unlit-drift |  |
| Kept | `unlit-kept` | 8 | `L` | a kept crosser | the-crossing, the-regard: none | unlit-the-long-held | drops carried things |
| Recognised | `unlit-recognised` | 9 | `W` | a recognised crosser | the-crossing, the-regard: none | unlit-the-recognised | drops carried things |

**The kept · Sulveth's, one per realm, quest supply stays on them**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Kept | `ossara-kept` | 9 | `W` | a kept traveller | the-rimwalk: none | ossara-left-behind | drops keepsakes |
| Kept | `grask-kept` | 9 | `W` | a kept miner | stiltmarsh, the-owing: none | grask-the-owed | drops crew tags |
| Kept | `azhen-kept` | 10 | `W` | a kept scholar | serrivet, thessivar: none | azhen-one-who-stayed | drops silenced plates |
| Kept | `nemhal-kept` | 10 | `W` | a kept priest | olmenneth, keshvaun: none | nemhal-the-untold | drops tokens and pages |

**Bosses · warleader chassis, 600 s respawn, gate item at 1.0**

| Kind | Key | Lvl | Icon | Name | Placed as (zone: modifier) | Replaces | Notes |
|---|---|---|---|---|---|---|---|
| Boss | `ossara-the-unclaimed` | 10 | `U` | the Unclaimed | the-rimwalk | unchanged |  |
| Boss | `grask-the-creditor` | 10 | `U` | the Creditor | the-owing | unchanged |  |
| Boss | `azhen-the-last-reader` | 10 | `U` | the Last Reader | thessivar | unchanged |  |
| Boss | `nemhal-the-first-mourner` | 10 | `U` | the First Mourner | keshvaun | unchanged |  |
| Boss | `unlit-the-first-held` | 10 | `U` | the First Held | the-regard | unlit-the-oldest-held | renamed |

**Two names that are not mobs.** "someone who has been waiting" becomes **the Waiting One** on
the template, which is what the guide already calls her and what a player can type. And the cast
had five people whose names began with *V*, one letter apart in two cases — Vess, Vesh, Vech,
Vance, Vane. Vesh is in seventy-four strings and stays. **Keeper Vess was renamed Keeper Adda**
on 2026-09-04 (five strings in `content/ossara/the-reaches.json`), so the two keepers in Ossara's
story stop sounding like one person.

### 3.3 Zone modifiers

What each spawner will carry once §4.1 exists. One word, inserted after the article, so "a rat"
placed here reads as shown. Chosen from the zone's own prose so the word is already in the room.

| Zone | Modifier | Reads as |
|---|---|---|
| `ossara.the-terraces` | terrace | a terrace rat, a terrace crow, a terrace hound |
| `ossara.brackenfell` | bracken / gorse | a bracken boar, a gorse lurcher, a bracken brigand |
| `ossara.the-rimwalk` | rim | a rim brigand, a rim lurcher |
| `grask.the-landing` | wharf | a wharf rat, a wharf hound |
| `grask.the-cutting` | claim | a claim thief, a claim cutpurse, a claim bruiser |
| `grask.stiltmarsh` | marsh | a marsh boar, a marsh lurcher, a marsh brigand |
| `grask.the-owing` | deep | a deep lurcher, a deep bruiser |
| `azhen.ummath` | hall | a hall servitor, a hall warden |
| `azhen.serrivet` | yard | a yard engine, a yard servitor |
| `azhen.thessivar` | tower | a tower warden, a tower engine |
| `nemhal.vurrach` | road | a road warden, a road bearer |
| `nemhal.olmenneth` | station | a station bearer, a station warden |
| `nemhal.keshvaun` | vigil | a vigil warden, a vigil bearer |
| `the-unlit.the-crossing` | — | the kept are already named by place |
| `the-unlit.the-regard` | — | as above |

The hubs carry no fodder and need none. The modifier is a *spawner* setting, so the same global
`brigand` can stand in Brackenfell as "a bracken brigand" and forty rooms later as "a rim brigand"
with nothing but the placement changed.

### 3.4 Talk for people without quests

The engine already has `greeting` (a cycled list, colour only). What is missing is the second half
of a conversation: **topics**. An NPC carries a small set of keywords; `talk vess stone` answers
with the line for `stone`; the greeting can mark the words that lead somewhere so they are
clickable, exactly as quest offers mark theirs. A topic may be **gated** on a character flag or a
completed quest, which is how the same NPC says more once the player has earned it — and how a
quest giver keeps driving the story *between* quests without a fourth quest to give.

The first pass, by thread, nine NPCs, about twenty-five lines:

| NPC | Open topics | Gated topic (on) |
|---|---|---|
| Hesper | yrriska, pigeons | the stone (`a1-1` done): who clears the Watch Stone |
| Corun | fire, khaldra | ember (`e1` done): the woman with the knife |
| Adda | sulveth, stone, gate | grask (`attuned.grask`): what looks back |
| Vech | book, gate, kheddran | first company (`a2-1` done) |
| Roan | sump, pumps | azhen (`attuned.azhen`): who built the mountings |
| Immeth | measure, square, tolerance | milestone (`attuned.nemhal`) |
| Vane | plates, regard | seat (`a3-3` done): why he did not look through it |
| Orrun | wall, west, eleven | edge (`a4-2` done): what the office is for |
| Aveth | dead, sevveth, sulveth | nemhalla (`a4-3` done) |
| The Waiting One | back, stones, forty | yrriska (`a5-2` done): does not answer, which is the answer |

Everything above is `content/` once §4.2 exists. No new quest, no new room.

---

## 4. Engine work

Two changes. Neither bumps `WorldBundle.CurrentFormatVersion` (16): in both cases a missing key
keeps its old meaning, which is the documented rule for not bumping.

### 4.1 Spawner name modifier — built

**Built 2026-09-04** (commit `950df27`). As specified below; the only departure is that the
change record has no default for the new field, so every call site states it.

**Shape.** `Spawner.NameModifier : string?` — one word, or null. Applied once, at spawn, into
`Mob.TemplateName`, which is already a per-instance snapshot; nothing downstream needs to know a
modifier exists. Rule: if the template name opens with an article, insert after it and re-pick
a/an ("an ox" + "old" → "an old ox"); otherwise prefix. A proper name (capital first letter) is
refused by the validator and the API rather than silently producing "Tessa marsh Roke".

**Why it is safe.** Name matching is derived from the display name (`NameMatch`), so "a marsh
brigand" answers to `brigand`, `marsh`, and the key for free. `MobLabel` numbers by display name,
so two brigands with different modifiers in one room are told apart without ordinals. Quests find
givers by `TemplateKey`, untouched. `look` reads the template description, untouched. Mobs are
in-memory only — there is no mob table — so the runtime side needs no migration.

**Files.** The same path `FightsAtLevel` took, which is the precedent for a per-placement setting:

| Layer | File | Change |
|---|---|---|
| Domain | `Spawner.cs` | property + remarks |
| Engine | `MobSpawner.cs`, `SpawnerSystem.cs` | new `MobNaming.Apply(name, modifier)` helper; pass through `Spawn(...)` |
| Engine | `WorldChange.cs`, `WorldMutationApplier.cs` | `UpsertSpawner` gains the field |
| Persistence | `SpawnerConfiguration.cs` + migration | `name_modifier` column, nullable |
| Server | `WorldBundle.cs`, `WorldImporter.cs`, `WorldExporter.cs`, `WorldWriter.cs` | `BundleSpawner.NameModifier` |
| Server | `BuilderContracts.cs`, `BuilderQueries.cs`, `BundleValidator.cs` | request/response fields; refuse on item spawners and proper names; the placement panel shows the composed name |
| Client | `builderApi.ts`, `SpawnerDialog.tsx`, `RoomSpawnersTab.tsx`, `TemplatePlacementPanel.tsx` | one text field; composed name in the list |
| Tools | `tools/Muwbta.Balance/Content/ContentSet.cs` | distinct key unchanged; no work unless it prints names |
| Tests | new `SpawnerNameModifierTests` beside `SpawnerLevelOverrideTests`; a `BundleValidatorTests` case; an API round-trip in `SpawnerLevelApiTests`' shape | |
| Docs | `PLAN.md` §4.8, `WORLD.md` §7.1 (the "fifty rows" paragraph becomes false and should say so) | |

### 4.2 NPC topics — built

**Built 2026-09-04.** As specified below, plus: a topic's answer may itself mark words, so one
topic can lead to the next; whatever a greeting did not link is listed dim beneath it; and
`ask <npc> about <topic>` is the same verb with the word taken out.

**Shape.** A new behavior-bag key, `topics`, beside `greeting`:

```json
"topics": [
  { "keyword": "stone", "text": "'Somebody clears the turf round the Watch Stone. Not me.'" },
  { "keyword": "grask", "text": "…", "requiresFlag": "attuned.grask" },
  { "keyword": "ember", "text": "…", "requiresQuest": "e1-adept" }
]
```

`talk <npc> <words>` today tries the words against the NPC's startable quests and then says "does
not know what you mean". The change is one more step before that fallback: match the words against
the NPC's *visible* topics (gates satisfied) the way quest words are matched, and answer. The
greeting may mark `<stone>` and `<gate>` with the same bracket syntax quest offers use
(`QuestOffer.Parse`), rendered as a `talk vess stone` link after the same round-trip check that
keeps quest links honest. `ask <npc> about <topic>` is a cheap alias.

**Why here and not a new system.** The quest shape is fixed and should stay fixed. Topics are the
thing that lets a *quest* NPC keep talking between quests (gated on their own chain) and a
*non-quest* NPC talk at all, with no journal entry, no reward, and no new table — it is
`jsonb` on the template, like everything else in the bag.

**Files.**

| Layer | File | Change |
|---|---|---|
| Engine | `MobBehavior.cs` | `TopicsKey`, `MobTopic` record, `TopicsOf(bag)`, add to `KnownKeys` |
| Engine | `QuestCommands.cs` | `Answer`: topic step before the fallback; `SmallTalk`/`Greet`: render marked words in greetings as links; `ask` alias |
| Server | `BundleValidator.cs` | refuse a `requiresFlag` no quest grants, a `requiresQuest` that does not exist, a keyword that collides with one of the NPC's own quest keywords |
| Client | `behavior.ts`, `MobBehaviorEditor.tsx` | topic rows: keyword, text, optional gate |
| Tests | beside `MobDisplayNameTests`: open topic, gated topic hidden then shown, collision refused, link round-trip | |
| Docs | `PLAN.md` §4.9 (the "talk is for everybody" paragraph) | |

### 4.3 Needs no engine work

- **The race roster and every rename.** Names live on templates; matching is derived; nothing
  authored references a mob's *name*. Keys are referenced by quests, spawners, loot and shop
  lists, so the rule for the content pass is **rename names freely, keep keys** except where a key
  is itself wrong (`grask-gullrat` names a gull).
- **Threads A–E.** All talk, all `content/`, once §4.2 exists.
- **Bosses.** Content.
- **The canon budget.** WORLD.md's embedded half is near its 12,000-token ceiling. This document
  stays out of it. If the assist should know the plot, that is a decision about what to *remove*
  from the canon, not a reason to grow it.

### 4.4 Order

1. Review this document; settle the roster names and the thread list.
2. ~~Build §4.1 and §4.2.~~ Done 2026-09-04.
3. Content pass, one realm at a time, Ossara first: roster and modifiers, then topics, then the
   bosses. Export, merge, check, import — the existing path. **Ossara done 2026-09-04**: the eight
   global kinds seeded from its rows, `ossara-kept`, thirteen spawners re-pointed with their zone
   words, topics and marked greetings on Hesper, Corun, Adda, Pell, Sesk, Old Ossa and Vesh, and
   the two unplaceable mites deleted. **Grask done 2026-09-04**: cutpurse, boar, brigand and
   lurcher reused from the global rows, `bruiser` added from its own, its trash kept at chassis
   5–6 as `grask-rat`, `grask-crow` and `grask-hound`, the owed become `grask-kept` "a kept
   miner", fifteen spawners re-pointed, and topics on Vance, Vech, Roan and the three shops
   (Kheddran has a voice: Vech is his man, Vance swore to him). **Azhen done 2026-09-04**: five
   bronze names become `azhen-servitor` and `azhen-engine` (the engine keeps the clamp arm's grip),
   the instrument warden becomes `azhen-warden`, the one who stayed becomes `azhen-kept` "a kept
   scholar", the flitter keeps its name as `azhen-flitter`, thirteen spawners re-pointed as hall,
   yard and tower, and topics on Immeth (the measure, and the milestone once Nemhal is attuned),
   Vane and Sask. **Nemhal done 2026-09-04**: the bier bearer and shroud binder become
   `nemhal-bearer` (the binder's grip kept), the vigil keeper `nemhal-warden`, the hollow hound
   `nemhal-hound` placed as "a hollow hound", the untold `nemhal-kept` "a kept priest"; the moth,
   lay brother and bell-ringer keep their plain names; ten spawners re-pointed as road, station
   and vigil; topics on Orrun, Aveth and Dell, which gives Sevveth his voice (Aveth prayed to him
   when she said the word) and Dell the milestone that answers Immeth. **The Unlit done
   2026-09-04**: a drift is `unlit-drifter`, the long held `unlit-kept` "a kept crosser", the
   recognised `unlit-recognised` "a recognised crosser", the boss `unlit-the-first-held` "the First
   Held", and "someone who has been waiting" is **the Waiting One** on the template; seven
   spawners re-pointed, three quest lines and one room reworded; the Waiting One has back,
   markers, forty and the face, and - once the stones are brought - Yrriska, which she answers by
   not answering. **The content pass is complete.**
4. ~~Re-read WORLD.md §7.1 and §10.4 against what landed and correct them.~~ Done 2026-09-04: §7.1
   was rewritten with §4.1, and §10.3 records the roster pass. WORLD.md was cut back to the world and
   the authoring rules on 2026-09-05; the engine arithmetic it used to tabulate lives in §10.2 now.
5. ~~Decide §4.5, and build it if the answer is yes.~~ Built 2026-09-04.

### 4.5 The canon belongs to the active world — built

**Built 2026-09-04.** As proposed below, with two details settled on contact: the canon rides only
in a *full* export, so a realm's content file stays reviewable prose and a scoped import leaves the
stored canon alone; and the assist reads it from `EngineOptions`, where the rest of the active
configuration already lives, so a request costs no query and an edit needs no cache to invalidate.
`tools/sync-canon.cs` is the way back into `docs/WORLD.md`. **Amended 2026-09-05:** the server
no longer embeds WORLD.md at all. `merge-bundles --canon docs/WORLD.md --into the-reaches` writes
the canon into the Reaches' configuration on the way to an import, and a configuration with none
tells the assist there is no world description rather than handing it the Reaches.

WORLD.md's canon half is compiled into the server as an embedded resource and sent to the builder
assist as its standing context (`Canon.cs`, budgeted by `CanonTests`). That ties the assist to the
Reaches: a second world built on this server would inherit Ilvaro and the Regard, and the only way
to change what the model knows is a rebuild. Raised in review on 2026-09-04; the proposal:

- **Store the canon as content, on the named configuration.** `GameConfiguration.Canon`, markdown
  text. The configuration already decides the starting room, which is what "the active world"
  means here, so activating one swaps the canon with it. The bundle carries it
  (`BundleGameConfiguration.Canon`, optional, no format bump), which makes `content/` the source:
  WORLD.md's canon half becomes something generated *from* the database rather than compiled
  *into* the server. The authoring notes below the marker stay in the repo, since they are
  process rather than world.
- **Edit it in the builder.** A Canon tab on the configurations panel: a markdown editor, a live
  token estimate against the assist's budget, and a warning past it. This is the "generate the
  output of the file to edit in game" ask.
- **Seed the assist from the live one.** `Canon.Prefix` reads the active configuration at request
  time, cached and invalidated on activate or save, and falls back to the embedded WORLD.md when
  the field is empty, so today's deployment does not change until somebody fills it in. The token
  budget stops being a constant in a test and becomes a setting beside the model name, because a
  swapped model has a different context window and the editor's estimate should be against the
  model actually answering.
- **Export.** `GET /api/builder/configurations/{key}/canon` returns the markdown; `export-bundle`
  writes it into the bundle; a small tool regenerates `docs/WORLD.md` above the marker from it.

Cost: one text column and migration, one bundle field, one endpoint, one panel, and `Canon.cs`
reading from the database. About the size of §4.1.

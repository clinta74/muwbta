# Separating the Reaches from the engine

The Reaches are one world authored for this engine. They are not the engine, and they are not the
starter set — that is `StarterWorldSeeder`, 753 lines in Persistence, which plants Aldenmoor on a
fresh database. This document is the plan for moving the Reaches into their own repository and
leaving an engine that builds, tests and ships without them.

**Decided.** The engine must stand alone: it builds and its suite passes with `content/` absent.
`abilities.json` stays with the engine — the four Paths are the game system, not the world.

---

## 1. What couples them today

| Coupling | Where | Severity |
|---|---|---|
| The realm maps are embedded in the server binary | `Muwbta.Server.csproj` `<EmbeddedResource Include="..\..\content\map\*.svg">`, with a build warning when the folder is missing | **Hard.** A build-time path from `src/` into `content/` |
| Six test files read `content/` off disk | `ShippedAbilities` (Engine, walks up to `Muwbta.slnx`); `AbilityContentTests`, `BundleFormatTests`, `BundleMergeTests`, `MobReachTests`, `QuestOfferContentTests` (Server) | **Hard.** The suite fails without the world |
| Every content tool compiles against the server | `check-bundle.cs`, `merge-bundles.cs`, `render-map.cs`, `export-bundle.cs` all open with `#:project ../src/Muwbta.Server/Muwbta.Server.csproj` | **Inverse.** See §6 |
| The canon source is a doc in this repo | `docs/WORLD.md`, read by `merge-bundles --canon`, written back by `sync-canon.cs` | Soft. Moves with the content |

`content/abilities.json` is the exception in that directory: the engine's own ability set, which
sixty-nine engine tests treat as shipped. It is not part of what leaves.

---

## 2. Maps become imported content

The strongest argument for this is not the coupling — it is that the current design cannot keep
the promise it was built to keep.

`MapSheets` embeds the sheets rather than reading a path because "a map that disagrees with the
world it draws is exactly the confusion a player cannot diagnose", and a build's own resources
cannot go missing or be edited underneath it. But the world does not come from the build. It comes
from the database, by import. **So the two can already skew today**: import a newer world against
an older image and the map quietly disagrees with the rooms. Embedding prevents a missing file. It
does not prevent the failure the remark is actually about.

A map that travels in the same bundle as the rooms it draws cannot drift from them, because they
arrive in the same operation.

**Shape.** A `mapSvg` on the world row, with the intrinsic width and height either authored beside
it or parsed from the SVG root — `MapSheet` already carries `World`, `Title`, `Width`, `Height`.
Only the SVG travels: the 9 MB of PNGs beside them are a `render-map` byproduct that nothing
serves.

**Costs, honestly.** Five sheets at 160–195 KB inline and JSON-escape into roughly double that, and
Ossara's realm file goes from 205 KB to something near 400 KB, which makes the bundles meaningfully
worse to read as diffs. Keeping them in a separate `maps.json` within the content repo avoids that
and still imports as one merged bundle. And `MapSheets` currently reads once at startup into a
`FrozenDictionary` — DB-backed needs an invalidation story, with the canon as precedent and its
warning attached: the canon deliberately does **not** re-warm on import, you activate.

**Format version.** This changes the bundle shape, so `WorldBundle.CurrentFormatVersion` moves
16 → 17. A mismatch is the one hard refusal in the import path, by design. `content/README.md`
states the version and `BundleFormatTests` asserts that it does, so the README moves with it.

**Done when** `Muwbta.Server.csproj` has no `content/` path in it at all, including the warning.

---

## 3. abilities.json moves engine-side

It is read by `ShippedAbilities` via a walk up to `Muwbta.slnx`, by `WorldHarness`, and by
`AbilityContentTests`. None of those care where it sits, only that it is findable and is the real
set. Move it out of `content/` — `src/Muwbta.Server/Content/abilities.json` or a top-level
`fixtures/`, whichever reads better — and update those three paths plus `content/README.md`.

The wrinkle is that abilities still have to reach a database, and they do that by import, as part
of a merged bundle. After the move, `merge-bundles content` no longer globs them up. Either the
merge takes the engine's ability file as an explicit input, or the engine ships it as its own
importable bundle. The second is cleaner and matches how the starter world already works.

---

## 4. Tests: rules here, prose there

This is the part that looks like a loss and is not.

`QuestOfferContentTests` reads shipped content deliberately, "for the reason the weapon balance
tests do: what needs asserting is the shipped prose, and a fixture would agree with itself." That
rationale is correct and it is also the argument for moving those particular assertions **out of
the engine repo**. An assertion about whether every authored quest carries its accept marker is a
statement about the content, not about the engine — and `check-bundle` already enforces exactly
those rules, including the topic-shadowing rule that a marker on `<rope>` would break.

So the split falls out cleanly:

- **Engine repo** tests the *rules* — `BundleValidator`, `BundleMerge`, the format, the importer —
  against small fixtures and against the starter world.
- **Content repo** runs `check-bundle` over the Reaches in its own CI, which is where a claim about
  the Reaches' prose belongs.

**The starter world becomes the engine's shipped fixture.** `tools/export-seed.cs` already writes
Aldenmoor out as a bundle, exactly as a fresh boot would plant it. Committing that file gives the
engine repo real shipped content to assert against — content it owns, not a fixture invented to
agree with the test. Note it needs a live Postgres to regenerate (it runs the real seeder against a
scratch database), so it is committed and refreshed deliberately, not built in CI.

**What actually happened, which is better than the plan.** The starter-world fixture was not needed:
once the generalising rules moved into `BundleValidator` — every quest offer marks exactly one
thing inside its sentence, and every mob a spawner places can be hit and can hit back — the engine
had no test left that wanted a world. Committing a derived Aldenmoor bundle with no consumer would
have been worse than not having one, so it was not done.

**What genuinely weakens.** `WeaponBalanceTests` and `QuestRewardBalanceTests` do not generalise —
"the epic tiers rank Blade, Warden, Hallow, Adept" is a claim about `epic-warden-1` and its
siblings, and asserted here it would fail against every other world. They went with the content, as
source rather than as a running suite, and they run again when §6 is decided. Until then those two
checks are not being made anywhere. Pretending otherwise would be the wrong way to write this down.

---

## 5. Extracting the repository

`git subtree split` over `content/` preserves the authoring history rather than landing the world
as one initial commit, which matters — that history is the record of how the world was authored.
`docs/WORLD.md` and `docs/STORY.md` go with it; WORLD.md is the canon source and STORY.md is its
companion.

Leaving behind: `content/abilities.json` (§3), and the tools (§6).

---

## 6. The dependency this creates, pointing the other way

Every content tool is a `#:project` app compiled against `Muwbta.Server.csproj`, and that is a
feature — `check-bundle` reads `RoomLayoutService.NonPlaceable` off the server rather than
transcribing it, so the checker cannot fall out of step with the engine it checks for.

After the split, **the content repo cannot validate itself without the engine checked out.** That
is the real cost of standing the engine alone, and it wants a deliberate answer:

| Option | Trade |
|---|---|
| Content repo checks out the engine in CI | Simplest, no packaging work, CI pins an engine ref |
| Ship the tools as a `dotnet tool` | Best ergonomics for an author; a real release process to maintain |
| Vendor the validator into the content repo | Fastest, and reintroduces exactly the drift `#:project` exists to prevent |

Not decided here. It does not block §2–§5, and it should be decided by whoever authors most.

---

## 7. Order

**Steps 1 to 4 are done.** Maps became imported content (format 17), the ability set moved to
`shipped/` — joined by the blocked-words list, which turned out to be the same argument — the
content rules that generalise moved into `BundleValidator`, and `content/` left for a repository
of its own with its history intact. What remains is §6, the dependency pointing the other way,
which is best decided by whoever authors most.

The record of the order, as planned:

1. **Maps as imported content** (§2) — removes the only build-time coupling. Format 17.
2. **`abilities.json` moves** (§3) — small, independent.
3. **Starter world committed as a fixture; content tests re-pointed or moved** (§4).
4. **Subtree split and the new repository** (§5) — only safe once 1–3 land, because it is the step
   that makes `content/` genuinely absent. Done: `git subtree split -P content` carried 48
   commits back to *"Gatetown, and a gate that has never opened"*; `WORLD.md` and `STORY.md`
   arrived as a copy, since a split takes a directory prefix and they lived under `docs/`. Their
   history stays in this repository, where nothing deletes it.
5. **Tooling ergonomics** (§6) — after, informed by using it.

Each step leaves the suite green on its own. Nothing here requires beta to be touched: beta runs
the Reaches, and this is a change to where their source lives, not to what has been imported.

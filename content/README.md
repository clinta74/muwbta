# Authored content

`WorldBundle` JSON for the Reaches. The design these transcribe is
[docs/WORLD.md](../docs/WORLD.md).

**The whole world is here**: five realms, eighteen zones, 238 rooms, 59 mob templates, 93 items,
100 spawners and all five acts.

**Merge them and import once** — see [Applying them](#applying-them). Importing the six files one at
a time works, but it has to be done in realm order (`ossara`, `grask`, `azhen`, `nemhal`,
`the-unlit`) and still produces dangling-exit warnings, because a realm's gate names a room in the
next one and each file's exits are applied before the next file's rooms exist.

| File | Zones |
|---|---|
| `ossara/gatetown.json` | Gatetown, hand-authored first and kept separate |
| `ossara/the-reaches.json` | the Terraces, Brackenfell, the Rimwalk |
| `grask/the-reaches.json` | the Landing, the Cutting, Stiltmarsh, the Owing |
| `azhen/the-reaches.json` | the Camp, Ummath, Serrivet, Thessivar |
| `nemhal/the-reaches.json` | the Hold, Vurrach, Olmenneth, Keshvaun |
| `the-unlit/the-reaches.json` | the Crossing, the Regard |
| `map/*.svg` | the drawn realm sheets, folded into the merge as `maps` and served to players |
| `unplaced.json` | templates nothing places yet — no rooms, no world, just the templates |

A bundle carries the world row above its zones, so any one file is enough to stand its realm up in
an empty database.

**`unplaced.json` is the file that makes the set a snapshot rather than a subset.** A scoped export
closes over references — the templates its spawners place, its quests name, its mobs drop, and its
shopkeepers stock — because a template has no zone of its own. A template nothing references yet is
therefore in no realm's file, and would be lost by anyone rebuilding from these. It is still content:
`spawn <item|mob> <key>` is a builder command, so an unplaced mob is one somebody can put down on
purpose — a test fixture, or something held back for later. Regenerate it with
`dotnet run tools/export-bundle.cs -- --unplaced -o content/unplaced.json`; a scoped export names
what it could not carry so you know when to.

**A configuration is the unit that travels.** A starter configuration tags the worlds it is for
(the Reaches are five worlds and one configuration), and
`dotnet run tools/export-bundle.cs -- --configuration the-reaches -o build/the-reaches.json` - or
the row's *Download bundle* in the builder's Setup tab - exports those worlds, the templates they
need, every ability, and that one configuration with its canon. The world and zone scopes stay for
the smaller job. A world belongs to one configuration, which is what makes "export this
configuration" have one answer.

**Progression is four conditional exits and nothing else.** Each realm's story chain ends in a
quest whose reward is a character flag, and the gate onward requires it (`PLAN.md` §4.15):
`attuned.grask`, `attuned.azhen`, `attuned.nemhal`, `attuned.the-unlit`. The gates are one-way
gated — you can always walk back the way you came, including out of the Unlit, where `recall` does
not work and walking is the only way home.

## Applying them

Merge these and the engine's own bundles into one, check it, then import it once:

```
dotnet run tools/merge-bundles.cs content shipped -o build/the-reaches.json --canon docs/WORLD.md --into the-reaches
dotnet run tools/check-bundle.cs build/the-reaches.json
POST /api/builder/import?dryRun=true    # what would happen; changes nothing
POST /api/builder/import                # do it
```

`--canon` writes the canon half of `docs/WORLD.md` into the named configuration as it merges, cut
at the marker and normalised the way the assist reads it. The document is the reviewed source and
the merged bundle is how it reaches a server; the server embeds no canon of its own, so a
configuration that arrives without one tells the assist there is no world description.

Both were Python and are now file-based C# apps, so checking this repo needs only .NET and Node —
the two things building it already needed. They are **shims**: every rule lives in `BundleValidator`
and `BundleMerge` and runs in `dotnet test`, which is the half that matters, since a check nobody
remembers to run reports nothing. What the port bought is that the rules now *reference*
`WorldBundle`, `RoomKey`, `RoomFlags`, `MobBehavior` and `QuestDialogue` instead of recovering them
with regular expressions over the C# — and reading a bundle through the real record catches things
raw JSON cannot, which is how a case-sensitivity bug that silently blanked every mob attack was
found.

The import endpoint requires the Builder role and cookie authentication, so scripting it means
logging in first. **Always dry-run first**: an import is **not atomic** — one entity is one loop
round trip and one transaction, so a failure part way through leaves everything before it applied.
Merging does not change that; what it changes is that every intermediate state is *valid*, and that
one dry run covers the whole world instead of six.

**`build/` is gitignored on purpose.** The merged file is derived, byte-for-byte reproducible from
these six, and six times the same words — committing it would make it a second source of truth that
drifts, invisibly, inside a diff nobody reads. Rebuild it when you import.

`check-bundle.cs` catches the three mistakes the dry run does not treat as failures — a one-way exit,
a room with no path to the rest of its zone, and a room declaring a zone it does not live in. It runs
without a server, so it belongs in an editor loop.

**Run it on the merged file, not the parts.** It can only judge an exit whose target is in the same
bundle, so per file the four cross-realm gates are the one part of the world it cannot check at all.
Run against the merge it found one on the first attempt — the Grask→Azhen gate left `west` and came
back `north`, where every other gate is a south/north pair — which is now fixed, and the merged world
checks clean at 462 exits.

**Warnings never block; only errors do.** That is deliberate, because some of what it warns about is
content's right to do: a **one-way exit can be the story** — a mirror you arrive through and cannot go
back out of — and no script can tell that from a slip. Read the warnings, then let the dry run decide,
since it is the only check that knows what is already in the target database. A room whose exit does
not come back should say so in its own prose; that part is authoring, not tooling.

## Three properties of the import path that shape how these are written

- **`formatVersion` must match the server exactly.** It is the one hard refusal in the whole path.
  Author against `WorldBundle.CurrentFormatVersion` — these files are at **18** — not against this
  sentence, which has now been wrong twice — and is now the last hand-kept copy, because a test
  fails if it disagrees with `BundleFormat.CurrentVersion` or if any tool starts carrying its own.
  The shims reference the constant and cannot drift; the merge refuses files that disagree.
- **Import is a merge, not a mirror.** Deleting an entity from a file does not delete it from the
  world, and renaming a room key produces *both* rooms on the next import. Removals are explicit
  `DELETE` calls.
- **Spawner GUIDs are content.** Re-importing is idempotent only because each spawner carries its
  own `id`. Minting a fresh one doubles that zone's population; never regenerate them.

## These files are an artifact, not the source of truth

Postgres is. From here the loop is to edit in the builder — where `/validate`, `/preview` and the
storyline graph actually run — and then **export over the file and commit that**. Re-export after
any session in the builder rather than hand-editing the JSON back into agreement.

Spawner ids in these files are `uuid5` over `zone|kind|template|rooms` rather than random, so the
generator that produced them is reproducible and a re-import cannot double a zone's population. An
export will replace them with whatever the database holds, which is fine — what matters is that
they are stable, not what they are.

The reason is on record: the `warden.last-stand` retune lived only in the database for a day while
`AbilityCatalogue` stayed stale, and the planned `DROP DATABASE` would have silently reverted it.
A file that is only written by hand drifts the same way, in the other direction.

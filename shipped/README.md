# What the engine ships

Content that belongs to the game rather than to a world, and is expected to be there whichever
world a server is running.

They arrive by different routes, and the difference is the point. The ability set is a bundle and
is imported like anything else, because a builder retunes abilities against a running server. The
blocked-words list is not a bundle and never travels in one — see below.

| File | What |
|---|---|
| `abilities.json` | Every ability all four Paths learn. No rooms, no world, just the set |
| `blocked-words.txt` | The default list of words nobody may say. Seeded once, then edited in the panel |

## Why this is not in `content/`

`content/` holds one authored world, on its way to a repository of its own
([docs/CONTENT-SPLIT.md](../docs/CONTENT-SPLIT.md)). These are not part of it and must not leave
with it.

The ability set is the clearest case. A server with no abilities is not a server missing content —
the level table already promised a character they know Kick at level 1, so a missing row is a
character who cannot cast what the game told them they have. `AbilityCatalogue` plants four, one
per Path, so a brand-new database has something castable on every Path; that is a floor, not the
game. The set is here, and `Program.cs` validates every row on every boot because an ability
naming an effect key this build does not have fails silently — the cast succeeds, the cost is
spent, and nothing happens.

## The blocked-words list

Not a bundle, and deliberately not carried in one. What a server refuses to hear is a property of
who plays there rather than of the world they play in: a list travelling with content would arrive
from whoever authored the realm, quietly replacing a decision the operator made — or export theirs
to whoever they sent a realm to. Format 18 is where it stopped travelling.

It is embedded in the server assembly, because the container publishes `/app/publish` with no
`shipped/` beside it, and written into any configuration that has none on startup. That is
add-only, like the ability reconcile: a configuration with a list keeps it, whatever it says, so an
operator who cleared theirs on purpose keeps it cleared. Editing the file changes what a *new*
server starts with; editing the panel changes what a running one enforces.

## Applying it

It merges alongside the world, and the same command takes both:

```
dotnet run tools/merge-bundles.cs content shipped -o build/the-reaches.json
dotnet run tools/check-bundle.cs build/the-reaches.json
```

Importing this file on its own works too — a bundle carrying only abilities is a legitimate thing
to apply, and is how a retune reaches a server that already has its world.

**The reconcile is add-only.** A key that already exists is left exactly as it is, whether it got
there from this file or from a builder's editor, so importing never quietly overwrites a retune
somebody made against a running server. The table is the authority; this is how the table gets
populated in the first place.

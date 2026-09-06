# What the engine ships

Content that belongs to the game rather than to a world. Everything here is imported the same way
authored content is — same bundle format, same endpoint, same merge — and is expected to be present
whichever world a server is running.

| File | What |
|---|---|
| `abilities.json` | Every ability all four Paths learn. No rooms, no world, just the set |

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

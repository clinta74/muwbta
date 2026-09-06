# Muwbta.Mcp

An MCP server that lets an AI agent author through the builder API: read content, validate a zone,
dig rooms, write prose, and check its own work.

Phases A and C of [docs/PAT-AND-MCP.md](../../docs/PAT-AND-MCP.md).

**One thing it will refuse:** a `BuilderRead` token cannot write, whatever is asked of it. That
refusal is the server's and this process cannot argue with it.

Writing to the world the game is currently serving is **allowed**, because that is what a builder
does from the editor every day and this holds a builder's token. `--protect-active` turns that off,
for pointing an agent at a server people are actually playing on.

## Setting it up

Build it once:

```bash
dotnet build tools/Muwbta.Mcp
```

Mint a token: sign in to the builder as an account with the Builder role, then **Setup → Access
tokens**. `BuilderWrite` to author; `BuilderRead` if you want an agent that can look and not touch.
Copy it when it is shown — it is not shown twice.

(A session cookie in `MUWBTA_COOKIE` still works, and was how this ran before tokens existed. It
expires when the browser session does, and it carries the whole account rather than the builder
surface alone, so it is the fallback rather than the way.)

Then register it with your agent. For Claude Code, `.mcp.json` in the repository root:

```json
{
  "mcpServers": {
    "muwbta": {
      "command": "tools/Muwbta.Mcp/bin/Debug/net10.0/Muwbta.Mcp.exe",
      "env": {
        "MUWBTA_URL": "http://localhost:5050",
        "MUWBTA_TOKEN": "muwbta_pat_..."
      }
    }
  }
}
```

That file holds a live credential, so `.mcp.json` is in `.gitignore`. Putting the server in your
user-level MCP configuration instead works too, and is arguably where a personal token belongs.

A token expires — every one does, by design. When calls start being refused, mint another and
revoke the old one from the same panel.

| Variable | Default | |
|---|---|---|
| `MUWBTA_URL` | `http://localhost:5050` | Base address of the server. |
| `MUWBTA_TOKEN` | — | A personal access token. This or `MUWBTA_COOKIE` is required. |
| `MUWBTA_COOKIE` | — | A session cookie value, if there is no token. Expires with the session. |
| `MUWBTA_COOKIE_NAME` | `muwbta.session` | If the deployment renamed it (`AuthOptions.CookieName`). |
| `MUWBTA_TIMEOUT` | `30` | Seconds. Raise it for a whole-world export. |

Flag: `--protect-active` refuses writes to the worlds the active configuration serves. Off by
default.

## What it exposes

Seven tools, not sixty — the argument is in §11 of the design document. `list_content` and
`get_content` are kind-tagged and carry most of the surface; the rest exist because they answer a
question rather than fetch a row.

| Tool | |
|---|---|
| `list_content` | Content of one kind: configuration, world, zone, room, mob, item, ability, quest, spawner. Rooms need a zone. |
| `get_content` | One piece of content by key. |
| `validate_zone` | What is structurally wrong with a zone — computed warnings, the hand-set unfinished flags, and the quest graph's cycles, unreachable quests and missing prerequisites. |
| `spawn_preview` | What a zone's spawns are worth once world and zone multipliers are applied. |
| `check_quest` | Whether a quest can actually be finished. |
| `where_used` | Which spawners carry a mob or item template, and where they sit. |
| `export_bundle` | A world or zone as import-shaped bundle JSON, for diffing before anything changes. |
| `upsert_content` | Creates or updates one piece of content. Decides create-vs-update by asking the server. |
| `delete_content` | Removes one piece of content. |
| `set_exit` | Points an exit at a room, or removes it. States the whole exit, so a lock left out is a lock removed. |

Configurations are readable and not writable: which one is active decides what the running server
serves and what every new player is told, so activating one stays a person's click. No tool wraps
those endpoints.

Two resources: `muwbta://canon` for the active configuration's canon, and
`muwbta://canon/{configuration}` for a named one — drafting usually happens against a world that
is not live. Both serve the same text `Canon.Resolve` feeds the in-editor assist, so an agent
working through MCP and a builder using the draft button cannot end up in different worlds.

The server also sends **instructions** at initialization (`ServerGuidance`), which most clients put
in front of the model as context. That is where the canon is recommended, the order of work is
suggested, and the limits of `validate_zone` are spelled out.

## The thing to be clear about

`validate_zone` is **structural only**. It catches dangling exits, unknown flags, missing
descriptions, ragged grids, inherited PvP, and quest chains that cycle or cannot be reached. It catches nothing
about voice, tone, or canon — a zone can pass every check and still read like it belongs to a
different game.

So the loop an agent can close on its own is the wiring, which is the tedious, expensive part at
zone scale. The prose still needs a person to read it. Plan the review accordingly: this shortens
authoring, it does not remove the author.

**That is on purpose, and it is not a gap to be closed.** The canon is offered — named in the
server instructions, first in the suggested order of work, available as a resource before anything
is written — and never enforced. A canon check in code would be a machine holding an opinion about
prose, and the moment one existed "it passed" would start being read as "it is good". An agent that
ignores the canon produces a zone that validates and reads wrong, which is precisely what a human
reviewer is for.

## What the first real zone changed

A zone was drafted through these tools — `aldenmoor.gorsefoot`, six rooms, two mobs, two items, a
quest — and it settled two design questions that argument had not.

**`dig_room` is gone.** It wrapped the walk-and-build endpoint, and `DigThrottle` paces that at one
dig every two seconds because it exists to stop a held-down movement key carving forty rooms. An
agent laying out a zone digs six times in half a second and was refused five of them, leaving the
rooms unlinked — which `validate_zone` then reported as four orphan rooms. The fallback,
`upsert_content` plus `set_exit`, produced the same zone and is the better path anyway: it writes
the title, the prose and the grid position in one call, where a dig leaves a placeholder to be
written over. It saved one call and some arithmetic, and cost a throttle and a tool slot.

**The world guard became opt-in.** It was on by default. The world's own canon says a new zone
stays under `aldenmoor.`, `aldenmoor` is the world being served, and so the guard blocked the
documented workflow on its first outing while preventing nothing. Live editing with no publish gate
is a decision this codebase already made (PLAN.md §7.4); a guard here was a publish gate invented
for agents alone.

The rate limits are now waited out rather than reported. A 429 from the builder bucket is pacing,
not refusal, and a client that gives up on one leaves a zone half-linked.

## The hole in the world guard

When it is on, the guard reads a world off a key, which works because the server enforces the
shape: a zone key is `world.zone` and a room key is `world.zone.room`. **Mobs, items, quests and
abilities have no world in their key** — they are global, and one of them may be spawned into the
live world by a spawner this cannot see. So retuning a mob that the live world uses is *not*
refused.

That is a real gap, not an oversight, and closing it would mean a placement lookup before every
template write. `where_used` is what an author has instead, and the server instructions tell an
agent to run it before changing a placed template.

## Known gaps

- **The end-to-end run is a script, not a test.** Every tool, the scope refusal, the guard in
  both states and the canon resource have been driven against a live server with a real minted
  token — but by hand. The checked-in tests cover the path table, the option parsing and the
  guard's two pieces of logic. Nothing in CI would notice if a route moved.
- **The kind-to-path table can drift from the routes it targets.** `BuilderEndpoints` could rename
  a route and nothing here would fail until an agent got a 404 and concluded the content did not
  exist. A test that enumerates the server's actual endpoints would close this and has not been
  written. This is the same gap as above from the other side, and the one worth closing first if
  Phase A turns into Phase B.
- **No spawner tooling beyond the generic upsert.** Placing a mob means writing a spawner by hand
  through `upsert_content`, which is the fiddliest thing an agent has to do here and the most
  likely to want a tool of its own once there is use to learn from.

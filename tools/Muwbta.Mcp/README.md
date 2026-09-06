# Muwbta.Mcp

An MCP server that lets an AI agent **read** the builder API: content, zone maps, validation
findings, quest reachability, and the world's canon.

This is **Phase A** of [docs/PAT-AND-MCP.md](../../docs/PAT-AND-MCP.md), and it exists to answer
one question before any of that document's credential work is written: *is agent-authored content
actually worth having?* Point an agent at a real world, ask it to draft a zone, and read what comes
back.

It cannot write. `BuilderClient` has no method that sends anything but a GET, so that is a property
of the code rather than a promise about which tools were registered — which matters, because the
credential it holds is a real builder's live session.

## Setting it up

Build it once:

```bash
dotnet build tools/Muwbta.Mcp
```

Sign in to the builder in a browser as an account with the Builder role, then copy the session
cookie: dev tools → Application → Cookies → `muwbta.session`. Copy the **value**; pasting
`muwbta.session=…` whole works too.

Then register it with your agent. For Claude Code, `.mcp.json` in the repository root:

```json
{
  "mcpServers": {
    "muwbta": {
      "command": "tools/Muwbta.Mcp/bin/Debug/net10.0/Muwbta.Mcp.exe",
      "env": {
        "MUWBTA_URL": "http://localhost:5050",
        "MUWBTA_COOKIE": "paste-the-cookie-value-here"
      }
    }
  }
}
```

That file holds a live credential for a builder account, so `.mcp.json` is in `.gitignore`. Putting
the server in your user-level MCP configuration instead works too, and is arguably where a personal
cookie belongs.

The cookie expires the way any session does. When every call starts answering "Not signed in",
sign in again and copy the new value — nothing here can renew it.

| Variable | Default | |
|---|---|---|
| `MUWBTA_URL` | `http://localhost:5050` | Base address of the server. |
| `MUWBTA_COOKIE` | — | **Required.** The session cookie's value. |
| `MUWBTA_COOKIE_NAME` | `muwbta.session` | If the deployment renamed it (`AuthOptions.CookieName`). |
| `MUWBTA_TIMEOUT` | `30` | Seconds. Raise it for a whole-world export. |

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

## Known gaps

- **The end-to-end run is a script, not a test.** Every tool and the canon resource have been
  driven against a live server on the seeded Aldenmoor world, but by hand: the checked-in tests
  cover the path table and option parsing only. Nothing in CI would notice if a route moved.
- **The kind-to-path table can drift from the routes it targets.** `BuilderEndpoints` could rename
  a route and nothing here would fail until an agent got a 404 and concluded the content did not
  exist. A test that enumerates the server's actual endpoints would close this and has not been
  written. This is the same gap as above from the other side, and the one worth closing first if
  Phase A turns into Phase B.
- **No write tools.** That is Phase C, and it waits on the personal access tokens in Phase B.

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
        "MUWBTA_URL": "http://localhost:5000",
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
| `MUWBTA_URL` | `http://localhost:5000` | Base address of the server. |
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
| `validate_zone` | What is wrong with a zone — computed warnings plus the hand-set unfinished flags. |
| `zone_map` | The room grid and exits, plus the storyline graph. |
| `check_quest` | Whether a quest can actually be finished. |
| `where_used` | Which spawners carry a mob or item template, and where they sit. |
| `export_bundle` | A world or zone as import-shaped bundle JSON, for diffing before anything changes. |

One resource, `muwbta://canon`: the active configuration's canon, the same text `Canon.Resolve`
feeds the in-editor assist. One source, so an agent working through MCP and a builder using the
draft button cannot end up in different worlds.

## The thing to be clear about

`validate_zone` is **structural only**. It catches dangling exits, unknown flags, missing
descriptions, ragged grids, inherited PvP, and quests that cannot be completed. It catches nothing
about voice, tone, or canon — a zone can pass every check and still read like it belongs to a
different game.

So the loop an agent can close on its own is the wiring, which is the tedious, expensive part at
zone scale. The prose still needs a person to read it. Plan the review accordingly: this shortens
authoring, it does not remove the author.

## Known gaps

- **No end-to-end test against a running server.** The tests cover the path table and option
  parsing; everything past the HTTP call has only been exercised by hand.
- **The kind-to-path table can drift from the routes it targets.** `BuilderEndpoints` could rename
  a route and nothing here would fail until an agent got a 404 and concluded the content did not
  exist. A test that enumerates the server's actual endpoints would close this and has not been
  written.
- **No write tools.** That is Phase C, and it waits on the personal access tokens in Phase B.

# Personal Access Tokens and an MCP Authoring Server — Design

> Status: planned, not scheduled. Written 2026-09-05. This is a design/evaluation document
> for a future build; no code has been changed.

## Context

The builder API is already the whole authoring surface — roughly sixty endpoints over worlds,
zones, rooms, mobs, items, spawners, quests, and abilities, every write funnelled through
`WorldEditor`. What it has that a generic CRUD API does not is a **correction loop**:
`zones/{key}/validate`, `/unfinished`, `/preview`, `/audit`, `quests/{key}/reachability`, and
`zones/{zoneKey}/storyline` all answer "is what I just wrote coherent?" in a form something other
than a human can read.

That is the whole argument for letting an agent author through it. The existing assist
(`IContentAssistant`, Ollama) drafts **one room** or **one prose blob** against a 16k window, with
extracted exemplars because a zone bundle is ~36,000 tokens. It cannot do the cross-cutting work:
dig a coherent fifteen-room layout, place spawners with sane multipliers, wire a three-step quest
chain, then read its own validation findings and fix them. An agent with its own large context,
reading `docs/WORLD.md` and `docs/STORY.md` as canon, can — if it can reach the API.

It cannot today. Authentication is a browser session cookie, and there is no non-interactive
credential of any kind. **That gap is the entire cost of this feature**; the MCP server itself is a
thin adapter over endpoints that already exist.

Per the project's remaining work, code is largely done and world content is the bottleneck. This
targets the bottleneck.

## Current state (verified 2026-09-05)

- **Cookie only.** `Program.cs:147-191` wires a single scheme: `AddAuthentication(Cookie)` +
  `AddCookie`, `HttpOnly`, `SameSite=Lax`, `Secure` unconditionally in Production, a fourteen-day
  sliding expiry from `AuthOptions.SessionTimeout`. Nothing in `src/` reads an `Authorization`
  header; no API key, bearer, or token type exists anywhere.
- **`HttpOnly` is load-bearing** (`Program.cs:152-154`): the browser's native `EventSource` cannot
  send an `Authorization` header, so the cookie is the only credential `/api/builder/stream` and
  the assist job streams can carry. Any token scheme is **additive** — it must not replace the
  cookie.
- **Three properties earned by `PrincipalRevalidator`** (`PrincipalRevalidator.cs`), re-checked on
  an interval (`AuthOptions.RevalidationIntervalSeconds`, default 60): a ban takes effect within
  the interval rather than in a fortnight; a role change is reflected into a live principal; a
  password change invalidates sessions issued before it (`PasswordStamp.Matches`). A token that
  only checks "is this hash in the table" silently loses all three.
- **Claims are defined in one place**: `AuthEndpoints.BuildPrincipal` (`AuthEndpoints.cs:379`) —
  `NameIdentifier`, `Name`, `Role` — shared with the revalidator precisely so two constructions
  cannot drift.
- **Policies derive from the role model**: `Policies.cs:24-31` builds `builder`/`moderator`/`admin`
  from `AccountRoleExtensions.RolesSatisfying`, so HTTP policy and the in-game command table cannot
  disagree. The roles are not one ladder.
- **Groups**: `/api/builder` requires `Policies.Builder` + `RateLimiting.Builder`
  (`BuilderEndpoints.cs:33-35`); `/api/builder/assist` the same policy with a tighter
  `RateLimiting.Assist` on the generating calls (`AssistEndpoints.cs:47-106`); `/api/admin`
  requires `Policies.Admin` (`AdminEndpoints.cs:30`).
- **Rate limits partition by account** for the builder policy (`RateLimiting.cs:131`,
  `AccountKey(http)`), which a token identity satisfies unchanged as long as it emits the same
  `NameIdentifier` claim.
- **Audit precedent**: `AdminAudit` / `AdminAction` (`Muwbta.Domain/Accounts/AdminAudit.cs`), which
  is deliberately not `content_audit` — "who made this person a builder" and "who edited this room"
  are different questions. Token issuance belongs to the first.
- **Passwords** are PBKDF2 via ASP.NET Core `IPasswordHasher<Account>` (`Account.PasswordHash`).
- **Client**: `client/src/builder/BuilderShell.tsx:25-33` owns the tab list; the `Accounts` tab is
  appended only for admins (`:82`).

---

# Part 1 — Personal Access Tokens

## 1. `AccessToken` entity + `access_tokens` table

New `src/Muwbta.Domain/Accounts/AccessToken.cs`:

- `Id` (UUIDv7, and **also the public lookup id** — see §2), `AccountId` (FK → `accounts`,
  ON DELETE CASCADE), `Name` (what the builder called it, max 64, for the revoke list)
- `SecretHash` (base64 SHA-256, see §3), `Scope` (`AccessTokenScope`, see §4)
- `CreatedAt`, `ExpiresAt` (**not nullable** — see §4), `LastUsedAt`, `RevokedAt`
- `PasswordChangedAt` snapshot taken at issue, mirroring `PasswordStamp`'s reasoning: a password
  change invalidates tokens issued before it, for the same reason it invalidates cookies.

`AccessTokenConfiguration` in Persistence; `DbSet<AccessToken> AccessTokens` on `MuwbtaDbContext`.
Index on `(AccountId)` for the list view; the primary key covers lookup. One migration,
**CreateAccessTokens**, adds only the table — no change to `accounts`.

## 2. Token format

```
muwbta_pat_<tokenId as 32 hex>_<43-char base64url secret>
```

Three parts, each for a reason:

- **The `muwbta_pat_` prefix** makes a leaked token greppable in a log and recognisable to secret
  scanners. It costs eleven characters and is the cheapest thing on this list.
- **The id half** gives an indexed primary-key lookup. Without it, verifying a token is a scan of
  every row hashing each one — the design that quietly stops working at a few hundred tokens.
- **The secret half** is 32 random bytes from `RandomNumberGenerator`, base64url, unpadded.

Shown **once**, at creation, in the response body and nowhere else. Never stored, never logged,
never re-displayable — the list view shows name, scope, created, expires, last used, and the id.

## 3. Hashing: SHA-256, deliberately not `PasswordHasher`

The secret is 256 bits of CSPRNG output, not a human password. It has no dictionary, no reuse
across sites, and no guessable structure, so the thing PBKDF2 buys — making an offline guess
expensive — buys nothing here. What PBKDF2 would cost is real: its iteration count on **every
request**, where the cookie path pays it once per sign-in.

Store `Convert.ToBase64String(SHA256.HashData(secretBytes))`; compare with
`CryptographicOperations.FixedTimeEquals`.

This asymmetry with `Account.PasswordHash` is the kind of thing a later reader "fixes". It gets a
comment on the field saying why, or it will not survive.

## 4. Scope: narrower than the account, and expiring

A token that simply carries its account's role is **a full account takeover when it leaks** — it
could change the password, and for an admin it could ban people. The token must be able to do less
than the person holding it.

```csharp
public enum AccessTokenScope
{
    /// <summary>Reads under /api/builder. Nothing else.</summary>
    BuilderRead = 0,

    /// <summary>Reads and writes under /api/builder. Not /api/admin, not /api/auth, not the game.</summary>
    BuilderWrite = 1,
}
```

Deliberately absent:

- **No admin scope.** MCP authoring needs `AccountRole.Builder`. `/api/admin` is bans, role
  changes, and account deletion; a non-interactive credential for that is a much worse trade for
  zero authoring benefit. An admin who also builds takes a builder-scoped token like anyone else.
- **No game/command scope.** A token that can play is a bot API — a different decision with
  different consequences, and it should not arrive as a side effect of this one.
- **No `/api/auth` reach.** In particular a token can never change a password or mint another
  token, so escalation from a leaked token to a permanent foothold is not available.

`ExpiresAt` is **required, with a ceiling** (90 days suggested, configurable). A non-expiring
credential is one nobody ever revokes, because nothing ever reminds them it exists. The issuing
form offers 7 / 30 / 90 days.

Enforcement is a claim plus a check in the handler, not a convention: the token is refused outright
for any path outside `/api/builder`, and `BuilderRead` additionally refuses any method other than
GET. Putting that in the handler rather than on sixty endpoints means a new builder endpoint is
covered the day it is written.

## 5. The authentication handler

New `src/Muwbta.Server/Auth/AccessTokenHandler.cs`, an
`AuthenticationHandler<AuthenticationSchemeOptions>` registered as scheme `"token"`:

1. No `Authorization: Bearer` header → `NoResult()` (not a failure — the cookie scheme may still
   apply).
2. Parse the three parts; reject a malformed token without touching the database.
3. Look up by id; reject if absent, `RevokedAt` set, or `ExpiresAt` past.
4. `FixedTimeEquals` the hash.
5. **Load the account and re-run the revalidator's three checks**: exists, not `IsBanned`, and its
   `PasswordChangedAt` matches the token's snapshot. Then take the role from the **account row**,
   never from anything stored on the token — a demotion has to bite here exactly as it does on the
   cookie path.
6. Refuse if the request path is outside `/api/builder`, or if the scope is `BuilderRead` and the
   method is not GET.
7. Build the principal with `AuthEndpoints.BuildPrincipal(...)` — the same one place — plus a
   `muwbta:scope` claim, with the authentication type set to the token scheme so `Policies` matches
   unchanged.

**A database read per request, unconditionally.** This is the opposite of the interval the cookie
path uses, and correct here: `PrincipalRevalidator` avoids per-request reads because a cookie sits
on the path of every command POST from every player. A builder token issues low-volume editing
calls against a policy already capped by `RateLimiting.Builder`. Paying the read buys immediate ban
and demotion with no `LastCheckedKey`, no ticket renewal, and no staleness window at all — strictly
simpler and strictly stronger than the cookie path it sits beside.

## 6. Scheme selection

Keep the cookie as the default for browsers and add a policy scheme that forwards on the presence
of the header:

```csharp
builder.Services.AddAuthentication(options => options.DefaultScheme = "muwbta")
    .AddPolicyScheme("muwbta", "cookie or token", options =>
        options.ForwardDefaultSelector = http =>
            http.Request.Headers.Authorization.Count > 0
                ? AccessTokenDefaults.Scheme
                : CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(/* unchanged */)
    .AddScheme<AuthenticationSchemeOptions, AccessTokenHandler>(AccessTokenDefaults.Scheme, null);
```

Every existing `RequireAuthorization(Policies.Builder)` then keeps working untouched, and
`HttpContext.TryGetAccountId` (`AuthEndpoints.cs:397`) works for both, because both emit
`NameIdentifier`. The SSE endpoints keep working from the browser on the cookie and additionally
become reachable by a token client that *can* set headers — which is every HTTP client that is not
`EventSource`.

## 7. Endpoints

Under `/api/auth/tokens`, requiring `Policies.Builder` **and cookie authentication specifically**
(a token cannot mint a token — §4):

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/auth/tokens` | The caller's own tokens. Never the secret. |
| `POST` | `/api/auth/tokens` | `{name, scope, expiresInDays}` → the token, once. |
| `DELETE` | `/api/auth/tokens/{id}` | Sets `RevokedAt`. Idempotent. |

Rate limited with `RateLimiting.Auth` rather than `Builder` — this is credential issuance, and it
should carry the tight limit for the same reason `POST /api/auth/password` does.

A cap of, say, ten live tokens per account, so a compromised session cannot quietly mint a hundred.

**Revoke-all on password change** falls out for free: `ChangePasswordAsync` already bumps
`PasswordChangedAt`, and the handler's step 5 makes every existing token fail from that moment with
no sweep required. Worth saying in the password-change response copy, because it is a surprise
otherwise.

Admins get a read-only view of *other* accounts' tokens under `/api/admin` (names and metadata,
never secrets) and the ability to revoke one. "Who holds a standing credential" is an
administrative question of the same kind as "who is a builder".

## 8. Audit and metrics

Two new `AdminAction` members — `TokenIssued`, `TokenRevoked` — written to `AdminAudit`, with actor
and target both the account itself for self-service and the admin as actor for an admin revocation.
`Before`/`After` carry the token name and scope.

`ServerMetrics` gains a counter for token auth outcomes (`accepted`, `expired`, `revoked`,
`unknown`, `banned`, `scope-refused`). A rise in `unknown` is somebody trying tokens.

## 9. Client surface

A **Tokens** section in the existing Accounts area (`client/src/builder/accounts/`), visible to any
builder rather than to admins only — which means `BuilderShell.tsx:82` either shows the tab to
builders with only that section inside, or the section moves to a small self-service panel reached
from the shell. The former is less new surface.

One-time-secret UX: a modal with the token in a copy field and a plain sentence saying it will not
be shown again.

---

# Part 2 — The MCP server

## 10. Shape and placement

A separate small process — `tools/mcp/` — not code inside `Muwbta.Server`. It is an HTTP client of
the builder API holding a token, so it has no reason to share a deployment, a database connection,
or a release cadence with the server. It also has to be runnable on a builder's own machine beside
their agent. Configuration: base URL and token from the environment.

## 11. Tool surface: about ten tools, not sixty

Sixty REST endpoints must **not** become sixty MCP tools. Agents degrade badly as the tool list
grows, and the REST surface is shaped for a React client that already knows the domain.

| Tool | Wraps |
|---|---|
| `list_content` | the `GET` collection endpoints, `kind` as a parameter (`world`, `zone`, `room`, `mob`, `item`, `quest`, `spawner`, `ability`) |
| `get_content` | the `GET /{key}` endpoints, same `kind` |
| `upsert_content` | `POST`/`PATCH` by kind — one tool, kind-tagged payload |
| `delete_content` | the `DELETE` endpoints |
| `dig_room` | `POST /rooms/{key}/dig` — layout is the operation agents do most, and the one with real semantics |
| `set_exit` | `PUT`/`DELETE /rooms/{key}/exits/{direction}` |
| `validate_zone` | `/zones/{key}/validate` + `/unfinished`, merged — the agent wants "what is wrong here", not two calls |
| `zone_map` | `/zones/{key}/preview` + `/zones/{key}/storyline` |
| `check_quest` | `/quests/{key}/reachability` |
| `export_bundle` | `/export`, for a diff before anything is activated |

`validate_zone` is the important one. Everything else is typing; that tool is what turns a
generator into an author.

## 12. Canon as resources, not prompt text

`docs/WORLD.md`, `docs/STORY.md`, and the active configuration's canon
(`GET /api/builder/configurations/{key}/canon`) are exposed as MCP **resources**. The agent reads
them when it needs them rather than carrying them in every request, and the canon the MCP server
serves is the same text `Canon.cs` feeds the local assist — one source, so the two cannot drift
into different worlds.

## 13. Relationship to the existing assist

It does not replace it. `IContentAssistant` is a **server-side** feature for a builder working in
the web editor with no agent of their own — it stays exactly as it is, drafting one room at a time
against Ollama. MCP is the other end: a builder who *has* an agent, working at zone scale. They
share canon and nothing else.

## 14. Blast radius

The agent must not edit the live world by accident. Two guards:

1. The token's account is a builder, so `/api/admin` is closed to it by policy before scope is even
   consulted.
2. The MCP server refuses to write to a world belonging to the **active** configuration unless
   started with an explicit `--allow-active` flag. Authoring happens in a draft world; activation
   stays a human click in the Setup tab.

---

## Phasing

**Phase A — read-only, no server change.** Ship `tools/mcp/` with only the read and validate tools,
authenticated by a cookie the operator pastes in, or run against a local dev server. Nothing new in
`src/`. This answers the only question that matters — *is agent-authored content actually worth
having?* — for about a day's work, and if the answer is no it stops here.

**Phase B — tokens.** §1–§9. The bulk of the work, and worth doing only after Phase A pays.

**Phase C — writes.** Turn on `upsert_content`, `dig_room`, `set_exit`, and `delete_content` behind
a `BuilderWrite` token and the active-world guard.

### The cheaper alternative to Phase B

If the only person doing this is the operator on the dev box, bind a loopback-only endpoint that
accepts a shared secret from configuration and issues a builder principal. Perhaps thirty lines: no
new table, no UI, no revocation story. It stops working the moment a second builder wants it from
their own machine, and it is a credential with none of §4's properties — so it is a bridge between
Phase A and Phase B, not a destination. Named here so the choice is deliberate rather than
rediscovered later.

## Open questions

1. **Should a token reach the SSE change feed?** It can, mechanically (§6). An agent watching
   `/api/builder/stream` to react to another builder's edits is interesting, and it is also a way to
   hold a connection open indefinitely against a rate limiter that does not count it.
2. **Per-world scoping.** `AccessTokenScope` gates by surface, not by content. A token limited to
   one world key is strictly better, and adding it later is a schema change — so it is worth
   deciding now whether the enum should instead be a small owned record carrying a `WorldKeys` list.
3. **`BuilderRead` as GET-only is a proxy for read-only.** Every mutating builder endpoint today is
   POST/PATCH/PUT/DELETE, so it holds — but by convention rather than by construction. An explicit
   read-only marker in endpoint metadata would be sturdier and is more work.
4. **The ten-token cap and the 90-day ceiling** are guesses. Both are configuration; both want a
   real number from use.

## What this does not do

- It does not let anything play the game non-interactively. §4 is explicit about that.
- It does not give an agent admin powers, and adding an admin scope later should be treated as a new
  decision rather than an extension of this one.
- It does not change the cookie path, the revalidator, or any existing endpoint's authorization.

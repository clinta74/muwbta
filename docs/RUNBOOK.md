# Recovery Runbook

What to do when something has already gone wrong. [DOCKER.md](DOCKER.md) and
[DEPLOY_NO_ENV.md](../DEPLOY_NO_ENV.md) cover setup — the half that gets written because it is the
half somebody needs while things are going well.

Every procedure here has been run. Where a step is untested, it says so.

---

## 1. Backups

### What runs

A `backup` sidecar in `docker-compose.prod.yml`, on the same network as `postgres`. It is a plain
`postgres:18` container running [tools/backup/backup.sh](../tools/backup/backup.sh) — same image as
the server, so `pg_dump` is never older than the database it is dumping.

Nightly at **03:00 UTC** (`BACKUP_AT_HOUR`), keeping **30** dumps (`BACKUP_KEEP`), in `./backups`
on the host as `muwbta-<ISO-stamp>.dump` (`pg_dump -Fc`, so `pg_restore` can be selective).

Each run dumps, **restores what it just dumped into a scratch database and compares exact row
counts for every table**, then prunes. A dump that does not restore is **deleted, not kept** — a
file that looks like a backup and is not one is worse than no file, because you find out during
the incident.

Two guards worth knowing about, both from failures hit while building this:

- **It refuses to run if `/backups` is not on a volume.** The dev container predated the mount, so
  dumps were landing in the container's writable layer: writable, plausible, and gone on the next
  `docker compose up`. From inside, that failure is invisible.
- **Pruning only touches files it wrote** (`muwbta-<stamp>.dump`). Hand-made dumps with other
  names are left alone.

### Take one now

```bash
docker compose -f docker-compose.prod.yml exec backup /scripts/backup.sh --once
```

Do this **before** every deploy that carries a migration. It costs seconds.

### Is the backup healthy?

```bash
cat backups/last-verified                              # stamp of the last verified run
docker compose -f docker-compose.prod.yml logs backup  # "ok", or the reason it was not
ls -la backups/
```

`last-verified` only advances on a run that dumped *and* restored. A stale stamp with a running
container means backups are failing — read the log.

---

## 2. The restore drill

```powershell
tools/restore-drill.ps1                                        # newest scheduled dump
tools/restore-drill.ps1 -Dump backups/muwbta-<stamp>.dump   # a specific one
```

Restores into a scratch database, **starts the server against it**, waits for `/health/ready`, and
reads the room count out of the loop's own startup line. Drops the database and stops the server on
the way out of every branch. Nothing it does touches the live database.

The sidecar already proves a dump restores. The drill answers a different question — *does the
application start against what came out of it?* — and that question has its own failure mode:

> **A dump can restore perfectly and still be unrecoverable.**

Which is not hypothetical here. See §3.

Run it after any migration, and whenever you want to believe the backups.

---

## 3. Restoring for real

### 3a. The normal case

```bash
docker compose -f docker-compose.prod.yml stop web        # single writer; nothing else may be up
docker cp backups/<dump> muwbta-postgres:/tmp/restore.dump
docker exec muwbta-postgres psql -U muwbta -d postgres -c 'drop database muwbta;'
docker exec muwbta-postgres psql -U muwbta -d postgres -c 'create database muwbta;'
docker exec muwbta-postgres pg_restore -U muwbta -d muwbta --no-owner --no-privileges /tmp/restore.dump
docker compose -f docker-compose.prod.yml start web
docker compose -f docker-compose.prod.yml logs -f web     # expect "Game loop starting with N rooms"
```

Stop `web` first and confirm it is down. §2.1 makes the loop the single writer, and a restore under
a live loop is two writers.

`--no-owner --no-privileges` because a recovery onto a rebuilt host is not performed by the role
that took the dump.

**The last line is the check.** "The container is up" and "the world came back" are different
claims; `Game loop starting with N rooms` is the second one.

### 3b. A dump older than a migration squash — **verified hazard**

**Squashed again on 2026-08-15.** The nine migrations that ran from `20260810145400_InitialCreate`
through `20260815022137_ConditionalExits` are now one baseline, **`20260815025112_InitialCreate`**.
Everything below about the 2026-08-10 squash still describes the shape of the problem; the ID to
repair *to* is the new one.

**Read this before reaching for the repair.** The recipe only works when the restored schema already
equals the new baseline — that is, when the dump was taken from a database that had applied
**every** migration through `ConditionalExits`. A dump from any earlier point restores a schema
missing columns the baseline creates (`room_exits.required_flag_key`, `quests.reward_flag_key`,
`characters.flags`, `spawners.fights_at_level`), and telling EF the baseline is already applied
leaves those columns permanently absent. The server starts and then fails on the first query that
names one.

Since any dump taken before the 2026-08-15 baseline carries the pre-squash history, **the honest
default for a database with no characters worth keeping is to drop and recreate it** rather than
repair the history:

```sql
DROP DATABASE muwbta; CREATE DATABASE muwbta;
```

Start `web`; the baseline builds the schema and the seeder plants abilities. Content comes back from
a `WorldBundle` import (§10.1 of `WORLD.md`), which is what that format is for.

---

A pre-squash dump — `dikuweb-full-2026-08-10.dump`, kept as a specimen and since deleted —
restored with no errors, and the server **would not start against it**:

```
Applying migration '20260810145400_InitialCreate'.
Npgsql.PostgresException: 42P07: relation "abilities" already exists
```

The migration history was squashed on 2026-08-10. That dump carries the six pre-squash IDs
(`20260807140817_InitialCreate` … `20260809135355_AddAccountMutedUntil`); the repo now starts at a
single `20260810145400_InitialCreate`. EF recognises none of the recorded IDs, concludes nothing has
been applied, and tries to build the schema from scratch on top of a fully populated one.

The rows are all fine. The history is what is wrong, so that is what to repair — restore as in §3a,
then **before starting `web`**:

```sql
-- ProductVersion must match the other rows in a current database (10.0.10 as of 2026-08-15).
-- The ID is the CURRENT baseline, which changes with every squash.
DELETE FROM "__EFMigrationsHistory";
INSERT INTO "__EFMigrationsHistory" VALUES ('20260815025112_InitialCreate', '10.0.10');
```

Start `web`. Anything after the baseline applies in order and the loop comes up.

**Only valid when the restored schema equals the baseline**, per the warning above — which for the
2026-08-15 baseline means a dump taken at or after `ConditionalExits`. It was true for the
2026-08-10 squash and **tested** there: the six later migrations applied cleanly and the loop
started with 15 rooms.

**If you squash again, every existing backup inherits this problem.** Note the new baseline ID here
in the same commit — as this section has now had to do twice.

---

## 4. A startup migration fails

**Symptom.** `web` restart-loops; nginx answers 502; the log ends in a `PostgresException` under
`StartupMigrator.RunAsync`. `StartupMigrator` retries transient failures with backoff, so a
connection blip recovers on its own — a loop that persists is not transient.

**The schema is not half-migrated.** No migration in this repo suppresses its transaction, so each
is atomic under Npgsql: a failure leaves the schema exactly at the previous migration. That makes
this a rollback, not a recovery.

1. `docker compose -f docker-compose.prod.yml stop web`
2. Read the actual `PostgresException`. It names the table and the constraint.
3. Point `web` back at the previous image tag and start it. The old build's migrations are already
   applied, so it starts.
4. Fix the migration, and rehearse it against a drill database (§2) before deploying again.

**Do not restore from backup for a failed migration.** The database is intact; restoring throws away
every write since the last dump to solve a problem that a redeploy solves.

**Do not hand-edit `__EFMigrationsHistory` to skip a failing migration.** It marks work as done that
was never done, and the next migration builds on a schema that does not exist. §3b is the one
exception, and it inserts a baseline rather than skipping anything.

---

## 5. Applying an ability retune

**Why this is not just a deploy.** The startup reconcile only plants rows that are **missing** — it
never updates and never deletes. That is deliberate: it is what stops a restart from reverting a
builder's work. The consequence is that changing the shipped set retunes new installs and reaches a
running server not at all.

**Where the set lives.** `content/abilities.json` — all 69 of them, a bundle like any other.
`AbilityCatalogue` in C# is four examples, one per Path at level 1, and exists only so an empty
database is not a game with no abilities at all. **Editing the catalogue is almost never what you
want.**

So a retune reaches a running server by import, by the builder UI, or by SQL.

### Getting a retune out of a server and back into the file

A change made in the builder lives in **that server's database and nowhere else**. It is not in
`content/abilities.json`, so the next fresh install does not have it. Bring it back:

1. **Builder → Setup → Import & export → "Download abilities only".** Ignores the World and Zone
   boxes; the file it gives you carries the abilities and nine empty collections.
   (`GET /api/builder/export?only=abilities`, if you would rather curl it.)
2. **Save it over `content/abilities.json`.**
3. **Re-merge**, so the single-file bundle agrees with the parts:

   ```
   dotnet run tools/merge-bundles.cs <path-to-reaches>/content shipped -o build/the-reaches.json
   dotnet run tools/check-bundle.cs build/the-reaches.json
   ```

4. **Commit it.** `dotnet test` validates the shipped set — a Path that stops unlocking, a timer
   with one ability on it, two room-wide controls that can be fired together — so a retune that
   breaks the set's shape fails there rather than in play.

Going the other way — a file into a server — is an ordinary import, and an abilities-only bundle
merges like any other: keys in the file are written and everything the file does not mention is
left alone. Importing one cannot empty a world.

> A **full** world bundle carries every ability too, whatever its scope, so it is another way these
> rows move. Know which file you are holding before you import it.

### The procedure

**Order matters.** The column has to exist before the SQL that fills it.

1. **Deploy the new build.** `StartupMigrator` applies pending migrations on boot; for this change
   that is `AbilityCooldownGroup`, which adds a nullable `cooldown_group` to `abilities`. Confirm it
   landed before going on:

   ```
   docker exec <pg> psql -U muwbta -d muwbta -c      "select column_name from information_schema.columns
      where table_name='abilities' and column_name='cooldown_group';"
   ```

   One row back means yes. No rows means the deploy has not taken; stop here (§4).

2. **Generate the patch**, naming only the abilities you mean to change:

   ```
   dotnet run tools/export-abilities.cs warden.shield-wall warden.last-stand      warden.ground-and-centre warden.unbreakable warden.the-last-wall      -o backups/ability-patch.sql
   ```

   Each statement is preceded by the ability's derived description, so what a `jsonb` blob will
   actually do is readable before you run it. **Read those lines.** They are the review.

3. **Snapshot the rows you are about to overwrite**, so you can diff and so you can put them back:

   ```
   docker exec <pg> psql -U muwbta -d muwbta -At -c      "select key, cooldown_group, effects from abilities where key like 'warden.%' order by key;"      > before.txt
   ```

4. **Apply.** The file is one transaction, so it is all-or-nothing:

   ```
   docker exec -i <pg> psql -U muwbta -d muwbta < backups/ability-patch.sql
   ```

5. **Diff.** Re-run the query from step 3 into `after.txt` and compare. Only the rows you named
   should differ, and only in the fields you meant.

6. **Restart `web`**, or edit any one ability through the builder. The cast path reads
   `AbilityCache`, which is loaded at boot and updated by builder edits — **it does not notice a
   direct SQL write.** Skip this and the table is right while the game keeps playing the old values.

### What to know before you run one

- **An upsert overwrites the whole row**, including any retune a builder made through the editor.
  That is why the tool names keys and does not export everything by default; `--all` exists for a
  database being rebuilt from nothing.
- **It is idempotent.** Re-applying against an already-patched database changes nothing, so a
  half-finished run is safe to repeat.
- **Diff with the changing field masked out.** Masking `cooldown_group` and diffing the rest is what
  caught a `maxHealth` of 1000 that a previous patch was believed to have fixed and had not. A diff
  that only looks at the field you meant to change will confirm what you already believe.

---

## 5a. Applying a content retune by SQL

The ability case above generalises. Anything written straight into Postgres — a retune, a retag,
a one-column correction — meets the same two traps, and they are the two steps people skip:

1. **Confirm the migration landed before running the SQL.** A patch that sets a column added by a
   migration that has not applied fails on the column, which is the good case; a patch that sets a
   column whose *meaning* changed in a migration silently writes the old meaning, which is not.
2. **Restart `web` afterwards.** The template and ability caches load at startup and do not notice
   a direct SQL write. Until the process reloads, the database is right and the game is wrong —
   which reads exactly like the patch not having worked, and invites running it again.

The weapon-slot retag was the worked example: after the `ItemSlotList` migration converted every
`slot` into a one-element `slots`, it set 12 weapons to either hand and 5 to two-handed. Two
properties are what made it safe, and a patch written here should copy both — it **set** values
rather than toggling them, so running it twice was running it once, and it **ended with a count**
that had to read 12 and 5 before you believed it had worked.

Prefer **importing `build/the-reaches.json`** when the world is otherwise up to date, since that
carries everything else authored since the last import and goes through the loop, the audit trail
and the validator. Prefer the SQL when it is not, and you only want the one change.

---

## 5b. Moving an account to another server

```bash
dotnet run tools/export-players.cs --account clint
dotnet run tools/export-players.cs --account clint --relocate ossara.gatetown.the-gate-yard
```

Writes a re-runnable SQL file and **touches nothing** — applying it is a separate command, printed
at the end. It carries the account, its characters (deleted ones included, because `deleted_at` is
the record of a retired name), everything those characters own walked recursively through
containers, and their quest journals. It carries no content at all.

**Run the world in first.** A character whose `room_key` names a room the target does not have is
relocated to the starting room on entry — graceful, logged as `RelocatedFromMissingRoom`, and not
what you wanted. `--relocate` is the answer when you already know the target lacks the room; the
tool prints the rooms the target needs either way.

Three things it refuses, all enforced in the generated SQL so they still hold when somebody applies
the file by hand: overwriting a character name held by another account, overwriting a different
account with the same email or username, and applying half of itself. **Re-running is destructive by
design** — it replaces the moved characters' items and quests, scoped to those character ids and
nothing else, which is what makes a second run produce the same state rather than a pile of ghosts.

Its column lists are read off the EF relational model rather than typed in. That is not tidiness:
the SQL script it replaced hand-copied four of them, and the sibling script that hand-copied nine
drifted from the schema five times — silently every time, because a column that stops being
emitted restores as its default (§3b, and BUGS.md #30).


## 6. Triage

| Symptom | Likely cause | First move |
|---|---|---|
| 502 from nginx, `web` restarting | Startup migration failed, or the database is down | `logs web`; §4 |
| 502, `web` healthy | Port mismatch — the client image proxies to `http://web:8080` | Check the `web` port comment in `docker-compose.prod.yml` |
| Players connected, commands do nothing | Loop wedged or stopped | `logs web` for the slow-pulse watchdog; restart `web`. Progress is saved (§11) |
| Everyone disconnected, page loads | SSE buffering somewhere in front | `proxy_buffering off`; `X-Accel-Buffering: no` on the stream |
| Content missing after a deploy | Migration data loss, or someone deleted it | `content_audit` has before/after per mutation. Prefer that over a restore |
| An ability retune did not take | SQL written, `AbilityCache` never reloaded | Restart `web`, or touch the ability in the builder. §5 |
| 504 on `POST /api/builder/import` | Proxy read timeout. Imports are batched now and take seconds, so suspect a proxy still on a short timeout — or a bundle far larger than the Reaches one | Raise `proxy_read_timeout` in **every** proxy in front, including your own TLS terminator. The import itself finishes regardless of the disconnect, so the world is not left half-written |
| Auth failing for everyone | Rate limit — behind a proxy every caller shares one address | Known weakness, §7 below |
| `last-verified` stale | Backups failing | `logs backup` |

---

## 7. Known gaps

Named rather than hidden.

- **The auth rate limit is site-wide behind nginx.** Every caller shares the proxy's address, so a
  single client can exhaust the bucket for everyone. Needs forwarded headers and a trusted-proxy
  list this repo does not have.
- **Backups are on the same host as the database.** A dump on the volume next to the cluster
  survives a bad migration and does not survive losing the host. Copying `./backups` off-box is not
  automated.
- **A ban or role change waits for revalidation** (~60s) rather than taking effect instantly.
- **No off-hours alerting.** The backup sidecar logs a failure; nothing pages anyone. §8's Grafana
  dashboard is where that would go.
- **The restore drill is Windows/PowerShell.** It runs where development happens, not on the
  production host.

---

## 8. Metrics

`EngineMetrics` publishes six instruments on the `Muwbta.Engine` meter; `docker-compose.prod.yml`
runs Prometheus and Grafana against them. Dashboard at `:3000`, and the four §11 targets are the
four panels.

`/metrics` is deliberately **not** proxied — nginx forwards only `/api/` and `/health`, so the
endpoint is reachable from inside the compose network and not from the internet. Keep it that way;
it is unauthenticated.

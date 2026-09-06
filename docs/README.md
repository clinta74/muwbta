# Documents

Everything written down about this project except the [top-level README](../README.md), which
stays in the root because it is the first thing a stranger reads.

| | |
|---|---|
| [PLAN.md](PLAN.md) | **The one to read.** Architecture, game design, the data model, the builder, the phase plan — and, more usefully, *why* each decision went the way it did. Where the reasoning behind anything in this repository lives. |
| _WORLD.md_ | **The world itself** — moved out with the content it describes (see [CONTENT-SPLIT.md](CONTENT-SPLIT.md)). The Reaches, the Unlit, the pantheon and every realm now live in their own repository, where the canon they transcribe lives beside them. Where `PLAN.md` says what the engine does, that said what went in it. |
| [ABILITIES.md](ABILITIES.md) | **The back half of every Path.** All four finish unlocking at level 20 and give nothing for the thirty levels after it; this is the plan for the thirty-two abilities that fill them. Design only, and content rather than engine — one validator constant is the whole code change. |
| [HISTORY.md](HISTORY.md) | What is finished. The phase checklists through 5, the notes from each build, and the postmortems — moved out of `PLAN.md` §8 so that document carries the design and the open work rather than an account of both. |
| [BUGS.md](BUGS.md) | The queue. A bug leaves this file when it has a fix **and** a test that would have caught it; the story of why it happened moves into `HISTORY.md`. |
| [PlayTestingNotes.md](PlayTestingNotes.md) | The inbox. Anything noticed while playing goes here, and is cleared as it is dealt with. |
| [PLAYTEST.md](PLAYTEST.md) | The playtesting apparatus: what it is, how a plan is written, and the content a plan builds for itself. |
| [MOBILE.md](MOBILE.md) | The mobile client — findings, the layout they argue for, and the phases. A proposal rather than a record. |
| [UX.md](UX.md) | A review of the client against best practice, style consistency, casing, spacing, resizability, and whether it is intuitive. Findings with evidence, ordered by what they cost a user. |
| [DOCKER.md](DOCKER.md) | Containers and deployment. Setup rather than recovery; the recovery runbook is still Phase 6 work. |
| [DOCKER_SETUP_SUMMARY.txt](DOCKER_SETUP_SUMMARY.txt) | A snapshot of what the Docker work added, from when it landed. Historical. |

## Two things worth knowing about these

**`PLAN.md` is long and that is deliberate.** It carries the argument for each decision alongside
the decision, so the question *"why is it like this"* has an answer that outlives whoever made the
call. Read the section, not the file. What it no longer carries is the *history* of arriving at
those decisions — that is `HISTORY.md`, and splitting the two is what keeps the design readable as
the finished work accumulates.

**Source comments cite these documents by name** — `PLAN.md §4.13`, `MOBILE.md §6` — rather than
by path. Over two hundred files do it, so the names are effectively an API. Renaming one of these
documents means a repository-wide edit; moving them into this folder deliberately did not, since
the citations name a document rather than a location.

*`DEPLOY_NO_ENV.md` is referenced from `PLAN.md` twice and does not exist. It was never written.*

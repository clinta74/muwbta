# The builder assist's models

> **The NAS no longer runs the assist (2026-10-01).** Its RTX 3060 went to Familiar, whose model
> and speech services need ~10.4 GB of the card's 12, leaving no room for the assist's ~8.4 GB.
> [`example/docker-compose.truenas.yml`](../../example/docker-compose.truenas.yml) now sets
> `Assist__Enabled: 'false'` and has no `ollama` service, and `docker-compose.truenas.gpu.yml` is
> gone. Both are in git history, in the commit before the one that added this note.
>
> The assist itself is untouched and still works in local development (`--profile assist`). The
> measurements below are kept as the record of what it cost, and as the starting point if it ever
> comes back to the NAS.

Ollama ran on the NAS for the builder AI assist (PLAN.md §13) — as a service in beta's compose
stack, of which `example/docker-compose.truenas.yml` is the template. This directory holds what
turns the stock model into the one the assist actually talks to, and the reasoning behind the one
number that matters.

## Why this exists at all

Ollama's default context is **4096 tokens**. The design puts a large, static canon prefix in front
of every request so its KV cache is built once and reused — which is what `OLLAMA_KEEP_ALIVE: -1`
and `OLLAMA_NUM_PARALLEL: 1` in the compose file are there to protect.

That prefix does not fit in 4096, and this is the shape of the problem rather than a near miss:

| source | chars | tokens |
|---|---:|---:|
| the Reaches canon, at its 2026-08 high-water mark | 33,970 | **10,183** |
| the same document's §10 — authoring process, not canon | 10,188 | ~3,050 |
| a whole zone bundle (`content/ossara/gatetown.json`) | 107,346 | ~35,800 |
| the room schema this repo generates | 2,840 | 946 |

The first row is measured — `prompt_eval_count` from a real call, not an estimate. Gemma 3
tokenises this prose at **3.34 chars/token**, denser than the 3.8 first assumed. Rows without a
bold figure are estimates at that ratio.

**Where the canon actually lives, and why this table is a high-water mark rather than a reading.**
It used to be `docs/WORLD.md` in this repo, compiled into the assembly. It is not either of those
things now: a configuration carries its own canon in `GameConfiguration.Canon`, and the Reaches'
source document went out with the content split into its own repository. See
[`Canon.cs`](../../src/Muwbta.Server/Assist/Canon.cs) for the whole of that reasoning — chiefly
that a server with no canon of its own used to be handed somebody else's world.

So the figures above are what the prefix cost when it was one file here, kept because the
*arithmetic* is what the 16k window was sized against. The canon has been cut down since. A real
builder draft on 2026-09-13 carried **7,339 prompt tokens end to end** — canon plus role, schema
and the request's own context — so the canon proper is now comfortably under the 10,183 recorded
here, and the budget below has more headroom than it was designed with. Slack is not a problem; it
just means the next re-measure should be of the live configuration rather than of a file.

What has not changed is the ratio. 3.34 chars/token is a property of how Gemma 3 tokenises prose
of this kind, and it holds whatever length the canon settles at — which is the entire reason
`Canon.CharsPerToken` is a constant rather than a bundled tokeniser.

An over-long prompt is **truncated, not refused**. So the failure mode is not an error anybody
sees — it is a model that has apparently read the world and misremembers two thirds of it, which
is indistinguishable from the model simply being bad at the job. That is the whole reason this was
the blocker: every claim the design makes about consistency rests on the prefix surviving.

## Measured, on Ollama 0.32.15

Run locally on a 20-thread desktop CPU, no GPU. **The NAS has 4 cores of a 9600K, so treat every
rate here as an optimistic ceiling** — the memory figures transfer, the speeds do not.

| | gemma3:4b | gemma3:12b |
|---|---:|---:|
| loaded size at 16k ctx (`ollama ps`) | 3.1 GB | **8.4 GB** |
| prefill | 134 tok/s | 55 tok/s |
| generation | — | **1.3–1.8 tok/s** |
| canon prefix, cold | — | 10,176 tok in **187 s** |
| canon prefix, cached | — | 10,197 tok in **4.4 s** |

Three things follow, and all three were open questions before.

**Sliding-window attention is active.** 8.4 GB loaded against ~8.1 GB of weights leaves a few
hundred MB of KV cache, not the 6 GB a full-window 16k would need. The memory question below is
settled in favour of the cheap row: 16k is comfortable inside `mem_limit: 12g`, and 32k (~2.4 GiB
of KV) would now fit too, if the canon ever outgrows the budget.

**Prefix caching works, and it is worth every setting spent on it.** The same prefix costs 187 s
cold and 4.4 s warm — **42×**. `OLLAMA_KEEP_ALIVE: -1` is not a tuning preference; without it the
first builder to click Suggest after an idle period waits three minutes here and considerably
longer on the NAS.

## Measured on the deployment

The numbers above are a 20-thread desktop. The NAS is 4 cores of a 9600K, and it is a different
feature there:

| | desktop | **NAS (4 cores, 9600K)** |
|---|---:|---:|
| bulk prefill (whole canon) | 55 tok/s | **~6 tok/s** |
| incremental prefill (115-token tail) | — | **3.96 tok/s** |
| generation | 1.3–1.8 tok/s | **0.93 tok/s** |
| canon prefix, cold | 187 s | **~25–30 min** |
| one draft, warm | ~170 s | **~3 min** |

The two prefill rates differ for a reason worth keeping: a long prompt amortises batch work that a
115-token tail cannot, so the per-request remainder is proportionally the more expensive of the two.

**Half an hour of cold prefill is why `AssistWarmUp` exists.** No request timeout can cover it and
no builder would wait; the first real attempt on beta timed out at ten minutes, and it took several
more presses before enough of the prefix had accumulated in llama.cpp's slot for a request to
finish. The server now does that prefill once at startup, on nobody's clock, and holds queued jobs
in a `Warming` state until it is done rather than starting a timeout they cannot survive.

Warm, the same machine is fine: a draft is about three minutes, of which only ~30 s is prefill of
the part that varies per request. The llama.cpp log shows the cache doing its job —
`cached n_tokens = 10456` of `task.n_tokens = 10571`, so only 115 tokens were new.

**Generation is the bottleneck, and it is slower than estimated.** 1.3–1.8 tok/s, against an
earlier guess of ~4.8. A 900-character description is ~230 tokens, so **one room takes about three
minutes on this machine and should be assumed worse on the NAS**. That is not a button with a
spinner behind it; it is a queued job with the result arriving later.

## Measured with a GPU

The dev box has an RTX 5070 Ti — 16 GB, so all 49 layers are resident and `ollama ps` reads
`100% GPU`. It is the same feature again and barely the same experience:

| | NAS (4 cores, CPU) | dev box (5070 Ti) | **NAS (RTX 3060 12 GB)** |
|---|---:|---:|---:|
| bulk prefill (whole canon) | ~6 tok/s | 635 tok/s | **1,223 tok/s** |
| generation | 0.93 tok/s | 21.5 tok/s | **34.4 tok/s** |
| canon prefix, cold | ~25–30 min | 16 s | ~8 s † |
| one draft, cold cache | — | — | **11.6 s** |
| one draft, warm | ~3 min | ~7 s | ~6 s † |

† Derived from the two measured rates above rather than observed end to end.

The 3060 figures are from a real builder draft, not a synthetic prompt: 7,339 prompt tokens in
6.00 s and 194 generated in 5.61 s, `total time = 11614.23 ms`. That request had its context
checkpoint invalidated and so paid a full prefill — hence the cold-cache row. A warm one skips
almost all of those six seconds, which is where the derived ~6 s comes from.

**The 3060 column is the one to trust, and it beating the 5070 Ti is a finding about the dev box.**
Generation is bandwidth-bound, and the 3060's 360 GB/s is about 40% of the 5070 Ti's ~900, so the
ordering should be the other way round. Against the theoretical ceiling — bandwidth over ~8.1 GB of
weights — the 3060's 34.4 tok/s is 78% of its 44, while the 5070 Ti's 21.5 is under 20% of its 110.
One of those is a card doing its job and the other is a card being held back by something. The
likeliest culprit is the WSL2 `/dev/dxg` path `docker-compose.gpu.yml` has to use, which the NAS's
native container toolkit does not pay for; the Ollama version gap (0.32.15 then, 0.34.0 now)
confounds it enough that the dev box row deserves re-measuring before it is quoted again.

The 3060 row is measured on Ollama 0.34.0 with `OLLAMA_FLASH_ATTENTION` still at its default of
false — so it is a floor, not a ceiling. The 12 GB holds the whole model at 16k context with ~3 GB
spare (`available="11.5 GiB"` against ~8.4 GB loaded), which is why `OLLAMA_KV_CACHE_TYPE: q8_0`
from the 6 GB-era overlay is deliberately *not* set here: quantising the KV cache buys layers only
on a card that cannot hold the model, and costs a little quality on one that can.

**The warm prefill barely registers**: 10,181 tokens in 0.9 s, because only the ~100-token tail was
actually new and the cache answered for the rest. That is the same prefix caching the whole design
rests on — it is simply no longer the thing you are waiting for.

At these speeds `AssistWarmUp` costs sixteen seconds and could arguably go. It stays: it is the NAS
it exists for, it is harmless here, and a warm-up that only runs on the slow machine is a warm-up
nobody tests.

## Applying it

From the repository root, on whichever machine runs the container:

```sh
sh tools/ollama/create-models.sh
```

It pulls the base, builds `muwbta-builder` from `Modelfile.builder`, and then **asserts** that
`num_ctx` came back as 16384 rather than trusting that it did. Rerun it whenever the Modelfile
changes; that is also how a changed parameter is applied.

Point the assist at `muwbta-builder`, not at `gemma3:12b`. Requesting the base by name gets you
4096 again.

### Locally, for development

`docker-compose.yml` carries the same service behind a profile, so a dev box can run the assist
without one being forced on anybody who only wanted a database:

```sh
docker compose --profile assist up -d
sh tools/ollama/create-models.sh
```

`appsettings.Development.json` already names `http://localhost:11434` and `muwbta-builder`, so
there is nothing further to configure — and that URL is why the dev compose publishes the port
while the NAS one deliberately does not. There, `web` is a container and reaches ollama by service
name across the compose network; locally the server runs on the host and has no such network.

**Expect the same half-hour warm-up on a first run** *if it is running on the CPU*, and expect it
on every restart of the container: the KV cache lives in the process, not the volume.
`docker compose stop ollama` between sessions is cheaper than `down`, which would also be fine —
the model itself is in a named volume and survives either.

### ...with the dev box's GPU

`docker-compose.gpu.yml` is an overlay, so a machine without a usable card still brings the assist
up on CPU:

```sh
docker compose -f docker-compose.yml -f docker-compose.gpu.yml --profile assist up -d
```

**It looks nothing like `docker-compose.truenas.gpu.yml`, and it has to.** That file uses
`deploy.resources.reservations.devices`, which is right on a Linux host running the NVIDIA
container toolkit. On a Windows box under Rancher Desktop it fails outright:

```
failed to discover GPU vendor from CDI: no known GPU vendor found
```

Rancher Desktop's WSL distribution ships no `nvidia-container-toolkit` and generates no CDI spec,
so there is no vendor for Docker to discover. What it *does* have is the card: WSL2 exposes it as
`/dev/dxg` and puts the driver libraries in `/usr/lib/wsl/lib`. Handing the container those two
things and pointing `LD_LIBRARY_PATH` at the mount is the whole mechanism — precisely what the
toolkit would otherwise automate.

Confirm it took by asking ollama what it found:

```sh
docker logs muwbta-ollama | grep "inference compute"
```

```
inference compute  library=CUDA compute=12.0  name=CUDA0
description="NVIDIA GeForce RTX 5070 Ti"  driver=13.2  total="15.9 GiB" available="14.7 GiB"
```

**`library=CUDA` is the line that matters.** Ollama falls back to the CPU silently and
successfully when the mounts do not land, so the failure mode is a working assist that is
dramatically slower than it should be, rather than an error anyone would notice.

At ~14.7 GiB available against ~8.1 GB of weights the whole model is resident, so none of the
CPU-side reasoning in this file applies: `ollama ps` should read **100% GPU**, and the half-hour
canon prefill is seconds. The NAS's `OLLAMA_KV_CACHE_TYPE: q8_0` is deliberately *not* copied here
— quantising the cache buys layers on a 6 GB card and there is nothing to buy at 16 GB.

## The memory question, now settled

16384 was chosen against a token budget (written out in `Modelfile.builder`). The reason it is not
32768 is RAM, and the arithmetic is worth having on hand because it decides whether the container
survives.

Gemma 3 12B: 48 layers, 8 KV heads, head dim 256. So per token, per layer, at f16:

```
2 (K and V) x 8 heads x 256 dim x 2 bytes  =  8 KiB
```

Where that lands depends entirely on whether the runtime gives you Gemma 3's sliding-window
attention, in which only every 6th layer attends to the full window and the other 40 are capped at
1024 tokens:

| | 16k ctx | 32k ctx |
|---|---:|---:|
| all 48 layers full (no SWA) | **6.0 GiB** | 12.0 GiB |
| 8 global full + 40 local capped (SWA) | **1.3 GiB** | 2.4 GiB |

Against `mem_limit: 12g` with ~7.5 GiB of weights resident, the top-left cell is 13.5 GiB and gets
the container OOM-killed; the bottom-left is 8.8 GiB and is comfortable. **Measured on 0.32.15, it
is the bottom row**: a 12B at 16k reports 8.4 GB loaded. The check, if it ever needs repeating on
different hardware or a newer Ollama:

```sh
docker exec muwbta-ollama ollama ps
```

The `SIZE` column includes the KV cache, and `CONTEXT` shows the window actually loaded — which is
a better check than `ollama show --parameters`, because it is what the runner did rather than what
the model declared. Weights plus ~1.3 GiB means SWA is working and there is room to spare; weights
plus ~6 GiB means it is not, and 16k is running without margin — in which case, before dropping
`num_ctx`, halve the cache instead, in the compose file's `ollama` service:

```yaml
      OLLAMA_FLASH_ATTENTION: '1'
      OLLAMA_KV_CACHE_TYPE: q8_0
```

Quantized KV needs flash attention, which is why both go together. This is a cheaper trade than a
smaller window: q8_0 costs very little quality on a cache and buys back half the memory, whereas
cutting `num_ctx` costs the thing the prefix exists for.

## Giving it a GPU

CPU-only is the deployment this was measured on, and everything above assumes it. A card changes
the arithmetic in a specific way worth understanding before buying one.

**Generation is memory-bandwidth bound** — every token reads all active weights, and the weights
are 8.1 GB. So the number that decides how much a card helps is whether it can hold them:

| | VRAM | bandwidth | 12B fits? |
|---|---|---|---|
| GTX 1060 6 GB | 6 GB | 192 GB/s | no — ~26–30 of 48 layers |
| RTX 3050 6 GB | 6 GB | 168 GB/s | no — and *less* bandwidth than the 1060 |
| Arc Pro B50 | 16 GB | 224 GB/s | yes, easily — but check Ollama's Intel support first |
| RTX 3060 12 GB | 12 GB | 360 GB/s | yes |

Under 8.1 GB means partial offload: the layers that did not fit are still evaluated on the CPU for
every token, so the gain is roughly proportional and the CPU limits in the compose file still
matter. Over it, the whole model is resident and generation goes from ~0.93 tok/s to something in
the teens or twenties.

**Prefill benefits more than generation, whatever the card.** It is compute-bound where generation
is bandwidth-bound, so even a partial offload should take a serious bite out of the half-hour
warm-up. That is the one place a small card earns its slot.

To turn it on, layer the overlay rather than editing the base file:

```sh
docker compose -f docker-compose.truenas.yml -f docker-compose.truenas.gpu.yml up -d ollama
```

`docker-compose.truenas.gpu.yml` carries the device reservation, the host prerequisites, and the
verification steps. The short version of verifying: `docker exec muwbta-ollama nvidia-smi` proves
the device arrived, and `ollama ps`'s PROCESSOR column says what fraction is actually on it.

**[GPU-SETUP.md](GPU-SETUP.md) is the step-by-step**, from installing the TrueNAS driver through
verifying and rolling back.

**Since the warm-up landed, a GPU is a comfort rather than a requirement.** The half-hour prefill
now happens once at startup with nobody waiting on it, and drafts run at about three minutes. A
card makes that pleasant; it is no longer the difference between the feature working and not.

## What is deliberately not in the Modelfile

**The canon prefix.** It could go in `SYSTEM`, and that would make it byte-identical across
requests by construction, which is exactly what prefix caching wants. It stays server-side anyway,
for two reasons: PLAN.md §13 wants the prompt to be "one thing in one place to tune", and baking it
means every canon edit is a model rebuild — with the failure being a *stale* world that still
answers, which is the same silent-wrongness this file exists to remove. The requirement it leaves
behind is real and belongs with whoever assembles it: **the prefix must be byte-stable across
requests**, or the cache misses and the minutes are spent again.

**Sampling parameters** beyond a sober default. `temperature`, `top_p` and friends are applied per
request and cost nothing to vary, so prose and schema-constrained calls can disagree about them.
Only load-time parameters belong here, because those are the ones a differing request silently
*reloads* on — and a reload throws away the prefix cache.

## For the schema work

One number above is a constraint on it: a whole zone bundle is ~36k tokens, so few-shot examples
cannot be whole bundles at any window this machine can afford. Exemplars have to be extracted
rooms, not files.

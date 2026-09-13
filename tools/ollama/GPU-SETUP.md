# Giving the builder assist a GPU

Putting a card in front of Ollama on the TrueNAS SCALE box. **Done on beta as of 2026-09-13**, with
an RTX 3060 12 GB; what follows is the procedure that worked and the two traps that cost an evening
on the way.

Read [`README.md`](README.md) for the measured numbers. The short version is that it is worth
doing: a builder draft went from about three minutes to about twelve seconds, and the startup canon
prefill from half an hour to roughly eight seconds.

**None of it is urgent.** Since `AssistWarmUp` landed, the cold prefill happens once at boot with
nobody waiting on it. A card makes the feature pleasant. It has not been the difference between the
feature working and not working since that warm-up existed.

---

## 0. Before you start: does the card hold the model?

The weights are **8.1 GB** (Gemma 3 12B, Q4_K_M), and about **8.4 GB** loaded at 16k context. That
single number decides everything:

- **Generation is memory-bandwidth bound.** Every token reads every active weight. If the weights
  do not fit in VRAM, the layers that did not fit are evaluated on the CPU for every token, and the
  speed-up is roughly proportional to the fraction that did fit.
- **Prefill is compute bound.** It benefits from a card whether or not the model fits, which is why
  even a partial offload takes a serious bite out of the warm-up.

| card | VRAM | bandwidth | holds 8.4 GB? |
|---|---|---|---|
| GT 1030 2 GB | 2 GB | 48 GB/s | no — not worth the slot |
| GTX 1060 6 GB | 6 GB | 192 GB/s | no — roughly 26–30 of 48 layers |
| RTX 3050 6 GB | 6 GB | 168 GB/s | no, and *less* bandwidth than the 1060 |
| Arc Pro B50 | 16 GB | 224 GB/s | yes — but confirm Ollama's Intel support first |
| **RTX 3060 12 GB** | 12 GB | 360 GB/s | **yes — this is what beta runs** |

The 3060 reports `total="11.6 GiB" available="11.5 GiB"`, so the whole model is resident with about
3 GB spare and `ollama ps` reads `100% GPU`. Full residency is what takes generation from 0.93
tok/s to 34.4; a 6 GB card would have bought a fraction of that.

---

## 1. Host: install the NVIDIA driver

TrueNAS SCALE **ships** the NVIDIA driver but does **not install it by default**.

1. Shut down, seat the card, boot.
2. **Apps → Configuration → Settings → Install NVIDIA Drivers.** This also installs the container
   toolkit, which is the part that lets Docker hand a device to a container.
3. Reboot.

Verify at a host shell before touching anything else:

```sh
lspci | grep -i nvidia                  # the card is seated and enumerating
nvidia-smi                              # the card, its driver, and ~12288 MiB
docker info | grep -i -A3 runtime       # `nvidia` must appear among the runtimes
midclt call app.gpu_choices | jq keys   # the card's PCI slot, beside any iGPU
```

**`docker info` listing `nvidia` is the gate.** Do not proceed past it — see §5, because the
failure mode is not a container that falls back to CPU.

### If the driver checkbox is greyed out

It greys out when the middleware does not detect a supported card. In order of likelihood:

- **A reboot is owed.** Hardware added since last boot, or a half-finished driver install.
- **The GPU is isolated for VM passthrough.** Check
  `midclt call system.advanced.config | jq '.isolated_gpu_pci_ids'`. A card in that list is
  deliberately withheld from apps, and shows exactly this signature — present in `lspci`, absent
  from `app.gpu_choices`. Clear it in **System Settings → Advanced → Isolated GPU Devices**.
- **A previous `docker.update` failed and left stale state.** See §1a, which is the one that will
  actually get you.

### 1a. The `docker.update` trap — read this before you tick the box

Enabling the driver calls `docker.update`, which **stops the Docker daemon and unmounts everything
under the apps dataset** before reconfiguring. If any of those unmounts fails, the job aborts
mid-teardown and reports:

```
CallError("Dataset 'Data/ix-apps' not found", 2)
```

That message is a lie of omission. The apps dataset is fine; the job simply died before it finished
looking. The real error is a few lines above it in `/var/log/middlewared.log`, and on beta it was:

```
libzfs.ZFSException: cannot unmount '/mnt/.ix-apps/app_mounts/nextcloud-official/
ix-nextcloud_data-ix-applications-backup-system-update--2024-12-07_03:42:27-clone':
no such pool or dataset
```

**A dataset whose name contains colons cannot be unmounted by libzfs**, and one created by iX's own
Kubernetes-to-Docker migration had been sitting there since 2024 waiting for the next
`docker.update` to trip over it. It reproduces from a plain shell with no middleware involved:

```sh
zfs unmount 'pool/…/name-with-2024-12-07_03:42:27-clone'
# cannot unmount '/mnt/…': no such pool or dataset
```

Once this happens, **apps will not start at all** — the same unmount pass runs at apps startup, so
every retry and every reboot fails identically. The fix is to rename the dataset colon-free:

```sh
zfs rename -u 'Data/ix-apps/app_mounts/…/…_03:42:27-clone' \
              'Data/ix-apps/app_mounts/…/stale-k8s-backup-clone-20241207'
```

`-u` skips the remount, which is the step that keeps failing. Reboot afterwards so it mounts under
the new name. `zfs set canmount=noauto` is the gentler alternative if you would rather not rename
something with data in it.

**So: before enabling the driver, audit for this.** It costs one command and saves an outage:

```sh
zfs list -r -o name Data/ix-apps | grep ':'
```

Anything that returns is a landmine. Deal with it first.

---

## 2. Apply: where the GPU settings actually go

**Beta is a TrueNAS Custom App**, so there is no shell `docker compose` invocation to add a flag to
— the YAML lives in the app's editor in the UI. Add both of these to the `ollama` service and save:

```yaml
  ollama:
    deploy:
      resources:
        reservations:
          devices:
            - capabilities: [gpu]
              count: 1
              driver: nvidia
    environment:
      OLLAMA_FLASH_ATTENTION: '1'
```

`deploy.resources.reservations.devices` is the modern spelling. `runtime: nvidia` still works and is
what older guides show, but it is deprecated and cannot say *which* devices or how many.

If you ever run this stack from a shell instead, the same settings exist as an overlay in
[`example/docker-compose.truenas.gpu.yml`](../../example/docker-compose.truenas.gpu.yml):

```sh
docker compose -f docker-compose.truenas.yml -f ../../example/docker-compose.truenas.gpu.yml up -d ollama
```

Every later compose command then needs both `-f` flags, or compose recreates the container without
the reservation. That footgun is the reason the app-YAML edit is the better path here, not the
overlay the earlier version of this document recommended.

### What is deliberately left out

**`OLLAMA_KV_CACHE_TYPE: q8_0`.** The overlay file carries it, because it halves the KV cache and on
a 6 GB card that buys about four more of the 48 layers. On a card that holds the whole model it buys
nothing and costs a little quality. Do not copy it onto the 3060.

**`cpus: 4`, `OLLAMA_NUM_THREAD` and `mem_limit: 12g` stay as the base file sets them.** They matter
less once the model is resident — the CPU is not evaluating layers any more — but they are still the
right ceiling for host-side buffers, and for the case where a future larger model does not fit and
falls back to a partial offload. Revisit them as a separate change, one variable at a time.

---

## 3. Verify

```sh
docker inspect muwbta-ollama --format '{{json .HostConfig.DeviceRequests}}'
```
`null` means the reservation did not reach the container and nothing below will tell you anything.

```sh
docker logs muwbta-ollama | grep "inference compute"
```
The line that settles it. What beta reports:

```
library=CUDA compute=8.6 name=CUDA0 description="NVIDIA GeForce RTX 3060"
driver=13.0 pci_id=0000:01:00.0 type=discrete total="11.6 GiB" available="11.5 GiB"
```

`library=cpu ... name=cpu` means Ollama found no accelerator — the reservation is absent or the
driver did not load. Note that a missing `nvidia-smi` **inside the container** proves nothing on its
own: that binary is injected under the toolkit's `utility` capability and can be legitimately absent
from a container with working CUDA. Believe this log line, not that binary.

```sh
docker exec muwbta-ollama ollama ps
```
Load a model first. **The PROCESSOR column is the real answer:** `100% CPU` means the card is
present but unused, `38%/62% CPU/GPU` is a partial offload, `100% GPU` is the whole model resident —
which is what a 12 GB card should show.

```sh
docker logs muwbta-web | grep -i "assist warm"
```
What it was actually worth, end to end.

---

## 4. Rolling back

Remove the `deploy` block from the app YAML and save. Nothing persistent changes; the model files
live on the `/mnt/ssd/ollama` bind mount and are untouched by any of this.

---

## 5. Troubleshooting

**The whole stack goes down, `could not select device driver "nvidia" with capabilities: [[gpu]]`**

This is the important one. **A device reservation the host cannot satisfy does not degrade to CPU —
it takes the stack down.** Compose starts containers, `ollama` refuses, and everything ordered after
it never starts. On beta that meant `postgres` and `prometheus` up, and `web`, `client`, `grafana`
and `backup` never reached.

So it is not a harmless thing to try speculatively. `docker info` must list `nvidia` **first**.

The diagnostic that distinguishes the two cases: if the reservation were present and unsatisfiable,
the container fails to start. If it started and chose CPU, the reservation is *absent* — check
`DeviceRequests` rather than assuming a driver problem.

**`nvidia-smi` works on the host but not in the container**

The reservation is not being applied. `docker inspect muwbta-ollama | grep -i -A5 devicerequest`.

**PROCESSOR says `100% CPU` with the card visible**

The model could not be laid out on the GPU at all. Usually VRAM already in use — `nvidia-smi` on the
host will show the other process. A display manager on the same card counts.

**`model 'muwbta-builder' not found` (404)**

Not a GPU problem. The derived model is built by [`create-models.sh`](create-models.sh), a deploy
step rather than a compose service, so nothing in the repo references it by name — which is exactly
why the muwbta rename missed it and left a `dikuweb-builder` behind on beta. Rebuild it; the base
blobs are already there, so it takes seconds and no download:

```sh
sh tools/ollama/create-models.sh
```

Or, without a checkout on the NAS, inline the Modelfile — see [`Modelfile.builder`](Modelfile.builder)
for what belongs in it, and check `ollama show muwbta-builder --parameters` reports `num_ctx 16384`.
A create that silently came back 4096 truncates the canon instead of erroring, which reads as the
model being bad at its job.

**It got *slower***

Possible on a very small card: if only a handful of layers fit, the PCIe round-trip per token can
cost more than it saves. `OLLAMA_NUM_GPU` can pin the layer count explicitly. Not a concern at 12 GB.

---

## See also

- [`README.md`](README.md) — the measured numbers on CPU, on the 3060, and on the dev box, plus the
  memory arithmetic and why the canon prefix is not baked into the Modelfile.
- [`example/docker-compose.truenas.gpu.yml`](../../example/docker-compose.truenas.gpu.yml) — the
  overlay form, for a stack driven from a shell rather than from the TrueNAS app editor.
- [`../../docker-compose.gpu.yml`](../../docker-compose.gpu.yml) — the dev box's WSL2 equivalent,
  which works completely differently and is worth re-measuring: it underperforms its hardware badly
  enough that the 3060 beats a 5070 Ti through it.

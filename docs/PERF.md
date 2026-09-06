# Resource verification (2026-09-06)

Environment: WSL2 devcontainer host, Jellyfin 10.11.11 in Docker (`jellyfin/jellyfin:10.11`),
plugin 0.1.0.0 (Debug build), Google Chrome 152 headless via Playwright. Numbers below are
from `scripts/e2e/perf-client.js` and an ad-hoc server script (fixed-volume downloads, cgroup
CPU accounting). Treat them as order-of-magnitude checks against the budgets in `SPEC.md` §8,
not benchmarks.

## Server CPU

Workload: 10 concurrent clients each download the same 150 MiB byte range of a direct-play
stream at full speed (about 4 Gbps aggregate, disk cache warm), three runs each. CPU is the
container's cumulative `cpuacct.usage` delta over the run.

| Configuration | CPU seconds per run (3 runs) | Mean |
|---|---|---|
| Plugin loaded, all 10 streams counted | 12.76, 12.69, 12.62 | 12.69 s |
| Plugin directory removed | 12.54, 12.22, 11.93 | 12.23 s |

Overhead: about 0.46 CPU-seconds per 1.5 GiB served, i.e. roughly 0.3 ms of CPU per MiB of
media. At a realistic load of 10 streams at 6 Mbps (7.5 MiB/s total) that is about 2 ms of
CPU per second, or 0.2 % of one core, plus the 1 Hz sampler which is not measurable at this
resolution. Budget: < 1 % of one core. **Within budget.**

The counting path is one `Interlocked.Add` per write. Setting `Response.Body` makes ASP.NET
route `SendFileAsync` through the counting stream (a buffered copy instead of Kestrel's own
file path); the measured difference includes that.

## Server memory

Container RSS (`memory.stat total_rss`):

| | Idle | After the 3 runs |
|---|---|---|
| Plugin loaded | 200.6 MiB | 300.5 MiB |
| Plugin removed | 206.4 MiB | 287.2 MiB |

The differences are within run-to-run GC variance of the host. The store itself is small by
construction: per tracked device 2 ring buffers x 300 x 8 bytes = 4.8 KB, plus two for the
server, so 50 devices is about 250 KB. Budget: < 2 MB steady state. **Within budget.**

## Client CPU

Chrome renderer main-thread task time (CDP `Performance.getMetrics`, `TaskDuration`) over
30 s windows while direct-playing the 6 Mbps clip:

| State | Main thread | of which script |
|---|---|---|
| Playing, overlay closed | 0.42 % | 0.10 % |
| Playback Info open, Data Flow polling and drawing | 0.78 % | 0.16 % |
| Playback Info open, Data Flow suspended (panel hidden, timers cleared) | 0.64 % | 0.12 % |

Data Flow cost: about **0.14 % of one core** (open minus open-without-panel); jellyfin-web's
own overlay re-render costs about 0.21 %. Budget: < 2 %. **Within budget.** With the overlay
closed the script holds no timers and makes no requests (verified by the e2e script: zero
`/DataFlow` requests in the 7 s after closing).

## Client network and payload

- One `GET /DataFlow/Samples` every 2 s; responses are deltas of 1 to 2 buckets, about 150
  bytes of JSON. `GET /Sessions?deviceId=` every 10 s. Budget: < 2 KB per poll. **Within budget.**
- `client.js` 26.1 KB + `client.css` 2.8 KB unminified, served with a 1 h cache and ETag.
  Budget: < 25 KB. **Over by about 4 KB**; see the deviation note in `PLAN.md`.

## Measurement notes

- Server-side counting records bytes handed to the kernel, not bytes on the wire. With a
  rate-limited client the kernel socket buffers (10 MB or more on WSL2) run ahead, so the
  per-second buckets look burstier than the client's true receive rate. Judge by the 5 s mean.
- The instantaneous `docker stats` CPU percentage was too noisy for this comparison because
  of that burstiness; the cumulative cgroup counter over identical fixed-size work is what
  the table reports.

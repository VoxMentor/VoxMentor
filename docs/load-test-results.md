# NBomber Load Test Results

Baseline throughput/latency results for the VoxMentor API (established ahead of `v2.0.0`, issue #153).

## How to run

The Core API must be running and reachable at `http://localhost:5253` (override with `VOXMENTOR_BASE_URL`). Start the compose stack (database, redis, ollama, judge0, judge0-worker, seq), then run the API (`dotnet run` in `src/VoxMentor.Api`) or point at a deployed instance.

```bash
cd src/VoxMentor.LoadTests
dotnet run -c Release
```

Reports (HTML + CSV) are written to `src/VoxMentor.LoadTests/results/` (gitignored).

## Latest baseline

| Scenario | Simulation | RPS | p50 | p95 | p99 | Notes |
|---|---|---|---|---|---|---|
| — | — | — | — | — | — | No run recorded yet |

**No baseline run has been checked in yet.** Run the scenarios above and record the HTML/CSV summary here, then update this table and `docs/CHANGELOG.md`.

See `src/VoxMentor.LoadTests/README.md` for the planned per-scenario targets (RPS, p50/p95/p99).

## Result storage

Raw run artifacts live in `results/` (gitignored). Summaries are recorded in this document and referenced from `docs/CHANGELOG.md`. Load tests are manual/nightly only and are **not** wired into PR CI.

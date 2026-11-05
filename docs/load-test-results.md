# NBomber Load Test Results

Baseline throughput/latency results for the VoxMentor API (established ahead of `v2.0.0`, issue #153).

## How to run

The Core API must be running and reachable at `http://localhost:5253` (override with `VOXMENTOR_BASE_URL`). Start the compose stack (database, redis, ollama, judge0, judge0-worker, seq), then run the API (`dotnet run` in `src/VoxMentor.Api`) or point at a deployed instance.

Before running the load test, register the test accounts used by `src/VoxMentor.LoadTests/Program.cs` against the Core API (one `POST /api/v1/auth/register` per account):

```bash
for i in 1 2 3 4 5; do
  curl -X POST "${VOXMENTOR_BASE_URL:-http://localhost:5253}/api/v1/auth/register" \
    -H "Content-Type: application/json" \
    -d "{\"fullName\":\"Load Test $i\",\"email\":\"loadtest$i@voxmentor.local\",\"password\":\"LoadTest!2026\"}"
done
```

(409 responses for already-registered accounts are expected and safe to ignore.)

Then run the load test:

```bash
cd src/VoxMentor.LoadTests
dotnet run -c Release
```

Reports (HTML + CSV) are written to `src/VoxMentor.LoadTests/results/` (gitignored).

## Configuration

Set `MockInterview:DailyLimit` high enough (e.g. `100`) in the Core API's `appsettings.json` or via environment variable so the mock-interview scenario can run without per-account daily-quota rejections. Default is 1/day per student.

## Latest baseline

| Scenario | Simulation | RPS | p50 | p95 | p99 | Notes |
|---|---|---|---|---|---|---|
| — | — | — | — | — | — | No run recorded yet |

**No baseline run has been checked in yet.** Run the scenarios above and record the HTML/CSV summary here, then update this table and `docs/CHANGELOG.md`.

See `src/VoxMentor.LoadTests/README.md` for the planned per-scenario targets (RPS, p50/p95/p99).

## tutor_ask baseline

No measured RPS/p50/p95/p99 baseline is recorded for `tutor_ask`. The tutor endpoint is rate-limited at 10 requests/hour/student, and the load test enforces a load-test-side zero-wait admission gate checked *before* login (at most one send per account per 6 minutes; a dropped copy performs neither an auth request nor a tutor call), so measured throughput is capped by the rate limit rather than capacity — equivalent to ~0.014 RPS across the 5 pre-registered accounts for both simulations (the 200/s spike injection far exceeds the gate; nearly all spike copies are dropped, so the gate is what caps tutor throughput under spike). The pacing model is documented in `src/VoxMentor.LoadTests/README.md` ("Tutor pacing") per issue #153. A real measured baseline requires running the scenarios against a compose stack with the tutor server rate limit raised; that run is not available in this environment, so no measured RPS/p50/p95/p99 values can be recorded here — the rate-limit cap above is the documented equivalent pending a stack run.

## Result storage

Raw run artifacts live in `results/` (gitignored). Summaries are recorded in this document and referenced from `docs/CHANGELOG.md`. Load tests are manual/nightly only and are **not** wired into PR CI.

# VoxMentor Load Tests (NBomber)

Baseline throughput/latency tests for the VoxMentor API ahead of `v2.0.0`. Built with [NBomber](https://nbomber.com) (`NBomber` + `NBomber.Http`) and run **manually/nightly only** — not wired into PR CI.

## Hot paths covered

| Scenario | Endpoints | Auth |
|---|---|---|
| `auth_login_refresh` | `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` | anonymous → cookie replay |
| `practice_submit_code` | `POST /api/v1/auth/login`, `POST /api/v1/student/submit-code` | JWT (cookie) |
| `mock_interview_start_answer` | `POST /api/v1/auth/login`, `POST /api/v1/mock/start`, `POST /api/v1/mock/{id}/answer` | JWT (cookie) |
| `tutor_ask` | `POST /api/v1/auth/login`, `POST /api/v1/tutor/ask` | JWT (cookie) |

Each virtual user is assigned one account from a pre-registered pool (round-robin by iteration). Each account has one cookie-enabled `HttpClient`, shared by all virtual users and scenarios that use that account. The `access_token`/`refresh_token` HttpOnly cookies set by login are therefore shared across those iterations. All four scenarios run under both load simulations.

## Load simulations

Both simulations run against every scenario (spike first, then sustained baseline).

### Sustained baseline — `Simulation.KeepConstant`

- 50 concurrent virtual users held for 5 minutes.
- Steady-state throughput/latency baseline.

### Spike — `Simulation.RampingInject`

- Injection rate rises linearly from 0 to 200 users/s over the full 2 minutes.
- Peak rate (200 users/s) is reached only at the end of the run.
- Validates behavior under burst load and recovery.

### Tutor pacing

- `tutor_ask` uses a per-account zero-wait admission gate: admission is checked *before* login, so a copy whose account slot is taken performs neither an auth request nor a tutor call — a truly local drop that respects the tutor rate limit of 10 requests/hour/student. A copy whose slot is free reserves the slot (+6 minutes by default), logs in, and sends one tutor API request; a failed login releases the slot so another copy can use it within the rate-limit window. Gate state resets at the spike→sustained phase boundary so a slot taken late in spike does not suppress sustained-phase sends. Drops wait one bounded second before completing so they don't spin as fast as NBomber can schedule them; a drop may finish after a phase boundary but carries no tutor request. With 5 pre-registered accounts, each phase generates at most ~5 tutor sends total (one per account) — approximately 0.042 RPS during the two-minute spike and 0.017 RPS during the five-minute sustained phase — well within quota. Those RPS figures apply to the default six-minute gate interval (`VOXMENTOR_TUTOR_GATE_MINUTES=6`); reducing or disabling the gate changes them. Warm-up is disabled for this scenario (`WithoutWarmUp`) because warm-up iterations would consume quota slots. The gate resets per phase and is not a continuous one-send-per-account-per-six-minutes limit; override with `VOXMENTOR_TUTOR_GATE_MINUTES` (set <= 0 to disable the gate for tutor capacity runs). Raising the server tutor quota does not increase measured capacity while the gate is in place — an uncapped capacity run requires raising/removing the server rate limit and the load-test-side gate together.

## Expected baselines (pre-v2.0.0 targets)

These are **planning baselines**, to be confirmed against the first checked-in run in `docs/load-test-results.md`.

| Scenario | Simulation | RPS (target) | p50 | p95 | p99 |
|---|---|---|---|---|---|
| `auth_login_refresh` | sustained | ~40–60 | < 100 ms | < 250 ms | < 500 ms |
| `auth_login_refresh` | spike | ~150–200 | < 200 ms | < 500 ms | < 1 s |
| `practice_submit_code` | sustained | ~20–30 | < 200 ms | < 600 ms | < 1.2 s |
| `practice_submit_code` | spike | ~80–100 | < 400 ms | < 1 s | < 2 s |
| `mock_interview_start_answer` | sustained | ~15–25 | < 300 ms | < 800 ms | < 1.5 s |
| `mock_interview_start_answer` | spike | ~60–80 | < 500 ms | < 1.2 s | < 2.5 s |
| `tutor_api` | sustained | ≤ ~0.017 | < 500 ms | < 800 ms | < 1 s |
| `tutor_api` | spike | ≤ ~0.042 | < 500 ms | < 800 ms | < 1 s |

> `practice_submit_code` and `mock_interview_*` hit Judge0 + Ollama, so they are the slowest paths and dominate p99. `tutor_ask` runs under both simulations but is admission-paced by a load-test-side zero-wait gate that resets at the spike→sustained phase boundary (at most one send per account per phase; non-quota copies are dropped without calling the API), so it is quota-bound rather than capacity-bound — its RPS target is the per-phase cap (~0.042 RPS spike, ~0.017 RPS sustained across 5 accounts) rather than a measured capacity; `tutor_api` `p50/p95/p99` target (< 1 s) covers the send path only, since drops are local and excluded from step stats. Results reflect the combined workload of all four scenarios running concurrently, not isolated scenario capacity.

## How to run

Requires a running compose stack with the Core API reachable at `http://localhost:5253` (overridable via `VOXMENTOR_BASE_URL`).

```bash
# 1. Start the stack (see docs/load-test-results.md for compose details)
cd src/VoxMentor.LoadTests

# 2. Run the load test
dotnet run -c Release

# 3. Reports are written to ./results (HTML + CSV). Compare against the baselines above
# and store a snapshot in docs/load-test-results.md.
```

## Notes / known ceiling

- ponytail: each virtual user is assigned a pre-registered account from a pool of five (round-robin by iteration). Each account has one cookie-enabled `HttpClient`, shared by all virtual users and scenarios using that account, so login/refresh cookies set by one iteration are visible to others on the same account. The account pool size caps concurrent distinct sessions — add more accounts to the pool in `Program.cs` if baseline variance demands it. Accounts must be pre-registered against the target API before running.
- `tutor_ask` and `mock_interview_start_answer` depend on Ollama/Judge0 being healthy in the stack; downtime shows as elevated latency/`503`, which is part of the baseline we want to capture.

## Results

Run artifacts live in `results/` (gitignored). A summary of each baseline run is recorded in `docs/load-test-results.md` and referenced from `docs/CHANGELOG.md`.

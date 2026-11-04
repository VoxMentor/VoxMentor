# VoxMentor Load Tests (NBomber)

Baseline throughput/latency tests for the VoxMentor API ahead of `v2.0.0`. Built with [NBomber](https://nbomber.com) (`NBomber` + `NBomber.Http`) and run **manually/nightly only** — not wired into PR CI.

## Hot paths covered

| Scenario | Endpoints | Auth |
|---|---|---|
| `auth_login_refresh` | `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` | anonymous → cookie replay |
| `practice_submit_code` | `POST /api/v1/auth/login`, `POST /api/v1/student/submit-code` | JWT (cookie) |
| `mock_interview_start_answer` | `POST /api/v1/auth/login`, `POST /api/v1/mock/start`, `POST /api/v1/mock/{id}/answer` | JWT (cookie) |
| `tutor_ask` | `POST /api/v1/auth/login`, `POST /api/v1/tutor/ask` | JWT (cookie) |

Each virtual user is assigned one account from a pre-registered pool (round-robin by iteration) and gets its own cookie-enabled `HttpClient`, so the `access_token`/`refresh_token` HttpOnly cookies set by login are replayed only within that user's session — sessions are never shared across users. All four scenarios run under both load simulations.

## Load simulations

Both simulations run against every scenario (spike first, then sustained baseline).

### Sustained baseline — `Simulation.KeepConstant`
- 50 concurrent virtual users held for 5 minutes.
- Steady-state throughput/latency baseline.

### Spike — `Simulation.RampingInject`
- Injection rate ramps to 200 users/s over a 1-second interval, sustained for 2 minutes.
- Validates behavior under burst load and recovery.

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
| `tutor_ask` | sustained | ~10–20 | < 400 ms | < 1 s | < 2 s |
| `tutor_ask` | spike | ~40–60 | < 700 ms | < 1.5 s | < 3 s |

> `practice_submit_code` and `mock_interview_*` hit Judge0 + Ollama, so they are the slowest paths and dominate p99. `tutor_ask` is rate-limited (10/h/student) — expect `429` once an account's rolling quota is exhausted; results reflect the combined workload of all four scenarios running concurrently in one invocation, not isolated scenario capacity.

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

- ponytail: each virtual user is assigned a pre-registered account and a dedicated cookie-enabled `HttpClient`; sessions are never shared. The account pool size caps concurrent distinct sessions — add more accounts to the pool in `Program.cs` if baseline variance demands it. Accounts must be pre-registered against the target API before running.
- `tutor_ask` and `mock_interview_start_answer` depend on Ollama/Judge0 being healthy in the stack; downtime shows as elevated latency/`503`, which is part of the baseline we want to capture.

## Results

Run artifacts live in `results/` (gitignored). A summary of each baseline run is recorded in `docs/load-test-results.md` and referenced from `docs/CHANGELOG.md`.

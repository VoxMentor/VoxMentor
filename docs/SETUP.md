# VoxMentor — Local Setup Guide

## Prerequisites

| Tool | Version | Why |
|---|---|---|
| **Docker Desktop** | Latest | Runs Redis, Ollama, Seq (PostgreSQL is now Supabase) |
| **.NET 8 SDK** | 8.0.x | Backend (.NET 8) |
| **Node.js** | 20.x | Frontend (Next.js 16) |
| **Python** | 3.12+ | Voice service (Whisper + Piper) |
| **Git** | Latest | Version control |
| **VS Code** | Latest | IDE (with C# Dev Kit + Copilot extensions) |

### Install Prerequisites

```bash
# Docker Desktop
# Download from https://docker.com/products/docker-desktop
# Start Docker Desktop before continuing

# .NET 8 SDK
# Download from https://dotnet.microsoft.com/download
dotnet --version  # should show 8.0.x

# Node.js 20
# Download from https://nodejs.org (LTS)
node --version  # should show v20.x

# Python 3.12+
# Download from https://python.org
python3 --version  # should show 3.12+

# Git
git --version
```

---

## Step 1: Clone the Repo

```bash
git clone https://github.com/your-org/voxmentor.git
cd voxmentor
```

---

## Step 2: Start Infrastructure

```bash
# Start infrastructure services (Redis, Ollama, Seq, OTEL collector, Voice)
docker compose up -d

# Verify all containers are running
docker compose ps

# Expected output:
# NAME                STATUS         PORTS
# voxmentor-redis     Up             6379
# voxmentor-ollama    Up             11434
# voxmentor-seq       Up             8081
# voxmentor-otel-collector Up       4317, 4318
# voxmentor-voice     Up             8001
```

> **Database:** PostgreSQL is now hosted on Supabase. Create a project at https://supabase.com, then copy the connection string into `.env` (see `.env.example`). The API runs migrations automatically on startup.

---

## Step 3: Pull Ollama Models (One-Time, ~2GB Download)

```bash
# Pull the LLM (chat model)
docker compose exec ollama ollama pull llama3.2:3b
# This takes 5-15 minutes depending on internet speed
# Downloads ~2GB

# Pull the embedding model
docker compose exec ollama ollama pull nomic-embed-text
# This takes 1-3 minutes
# Downloads ~270MB

# Verify models are loaded
docker compose exec ollama ollama list
# Expected output:
# NAME                SIZE
# llama3.2:3b        2.0 GB
# nomic-embed-text    274 MB
```

---

## Step 4: Configure Supabase & Run Migrations

```bash
# 1. Create a Supabase project at https://supabase.com
# 2. Get the connection string from Project Settings → Database
# 3. Set the password and connection string for this shell. The .NET host does
#    NOT read .env, so `dotnet ef` and `dotnet run` only see exported variables
#    (user secrets and appsettings.Development.json work too - see .env.example).
#    Prompting keeps the password out of shell history and process arguments.
read -rsp "Supabase password: " PGPASSWORD && export PGPASSWORD
export ConnectionStrings__DefaultConnection="Host=<supabase-host>;Port=5432;Database=postgres;Username=postgres;Password=${PGPASSWORD};SslMode=Require;"
#    For IPv4-only networks without the Supabase IPv4 add-on, copy the session
#    pooler host from Project Settings → Database (region-based, e.g.
#    aws-0-eu-central-1.pooler.supabase.com) and keep port 5432; 6543 is
#    transaction mode. Username becomes postgres.<project-ref>.
export PSQL_URI="postgresql://postgres@<supabase-host>:5432/postgres?sslmode=require"

# 4. Enable pgvector BEFORE migrating. No EF migration runs CREATE EXTENSION,
#    and the first vector(768) column needs it. `-e PGPASSWORD` with no value
#    passes the exported variable into the container instead of the argv.
docker run --rm -i -e PGPASSWORD postgres:16 \
  psql "$PSQL_URI" -c 'CREATE EXTENSION IF NOT EXISTS vector;'

# 5. Run EF Core migrations against Supabase (reads the exported connection string):
dotnet ef database update --project src/VoxMentor.Infrastructure --startup-project src/VoxMentor.Api

# 6. Seed reference data (DSA concepts, questions, BKT parameters):
#    These SQL scripts are idempotent and safe to re-run.
docker run --rm -i -e PGPASSWORD postgres:16 \
  psql "$PSQL_URI" -f /dev/stdin < scripts/seed-dsa-concepts.sql

docker run --rm -i -e PGPASSWORD postgres:16 \
  psql "$PSQL_URI" -f /dev/stdin < scripts/seed-questions.sql

docker run --rm -i -e PGPASSWORD postgres:16 \
  psql "$PSQL_URI" -f /dev/stdin < scripts/seed-bkt-parameters.sql

# 7. Verify seed data
docker run --rm -i -e PGPASSWORD postgres:16 \
  psql "$PSQL_URI" -c 'SELECT COUNT(*) FROM "Concepts";'
# Should return: 50

docker run --rm -i -e PGPASSWORD postgres:16 \
  psql "$PSQL_URI" -c 'SELECT COUNT(*) FROM "Questions";'
# Should return: 100
```

> **Alternative (psql locally):** If you have `psql` installed, you can run the SQL files directly
> in the same shell (the exported `PGPASSWORD` is used, nothing goes in the argument list):
> ```bash
> psql "$PSQL_URI" -f scripts/seed-dsa-concepts.sql
> psql "$PSQL_URI" -f scripts/seed-questions.sql
> psql "$PSQL_URI" -f scripts/seed-bkt-parameters.sql
> ```

### Nightly background jobs

Two Hangfire jobs run inside the API, registered at startup as recurring jobs:

| Job | Cron (UTC) | What it does |
|---|---|---|
| `bkt-parameter-tuning` | `0 2 * * *` | EM-optimises each concept's BKT slip/guess/learn from all users' scored submissions (concepts with 50+ submissions) |
| `spaced-repetition-decay` | `0 3 * * *` | Mastery rows idle more than 7 days lose 5% per idle day, down to a 0.1 floor |

They need a Hangfire connection string. `ConnectionStrings:Hangfire` wins, and
the API falls back to `ConnectionStrings:DefaultConnection`, so a normal dev
setup needs no extra config — the jobs appear under **Recurring jobs** on
`/hangfire` and the startup log confirms both registered. If neither
connection string is set the API logs a warning and the jobs silently do not
exist; textbook uploads also return 500 in that state.

Notes:

- `/hangfire` is mapped in Development only and needs the `PlatformAdmin` role.
- Both jobs read and write the database directly, cross-tenant, so the global
  user filter does not apply to them.
- Neither job calls Ollama — the EM tuning is pure maths over stored
  submissions. (`nomic-embed-text` is only needed by the textbook ingestion
  pipeline, not by these two.)
- `VoxMentor.Gateway` deliberately runs no Hangfire worker; all job execution
  happens in the API.

---

## Step 4b: Seed the RAG corpus (optional but recommended)

The tutor answers questions out of `TextbookChunks`. `scripts/seed-corpus.py`
fetches the curated GeeksforGeeks DSA articles listed in
`scripts/corpus/articles.json`, groups them per concept, and uploads one file
per concept through `POST /api/v1/admin/textbook/upload` — 50 concepts, ~680
embedded chunks. No copyrighted text is committed; only URLs are.

```bash
pip install -r scripts/requirements-seed.txt

# Dry run: fetch, map and report chunk counts without uploading
python scripts/seed-corpus.py plan

# Upload (needs a ContentAdmin/SuperAdmin account; register only grants Student)
VOXMENTOR_ADMIN_EMAIL=admin@example.com VOXMENTOR_ADMIN_PASSWORD=... \
  python scripts/seed-corpus.py seed

# Re-run without duplicating what is already there
python scripts/seed-corpus.py seed --clean

# Check the acceptance criteria (>= 500 chunks, >= 5 per concept, cosine search)
python scripts/seed-corpus.py verify
```

Uploads go through the `ManageContent` policy, so grant the role once:

```sql
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id" FROM "AspNetUsers" u, "AspNetRoles" r
WHERE u."Email" = 'admin@example.com' AND r."Name" = 'ContentAdmin';
```

You can also drop your own prep material (e.g. a CTCI PDF converted to text)
into `scripts/corpus/source/` (gitignored) — those files carry no concept label,
so the script picks the nearest concept with keyword matching plus TF-IDF.

Offline tests for the mapping/chunking logic:

```bash
python -m unittest scripts.test_seed_corpus
```

---

## Step 4c: Demo seed data (optional)

Populate a fresh stack with a realistic demo user, two job descriptions, mastery/readiness values, two completed mock interviews with AI reviews, and three tutor sessions — so the dashboard is never empty on first login.

**Prerequisite:** Step 4 must have run first (Supabase reachable, pgvector enabled, migrations applied, 50 DSA concepts + 100 questions seeded via `scripts/seed-*.sql`). The seeder fails fast with a clear message if concepts are missing.

```bash
# One-liner (uses the Supabase connection exported in Step 4; runs migrations if needed)
dotnet run --project src/VoxMentor.Api -- --seed-demo

# Or enable via config for hands-off dev boots (set in appsettings.Development.json or env)
# Seed__Demo=true
# Seed__DemoPassword=<your-demo-password>  # required; no default shipped
# Then just: dotnet run --project src/VoxMentor.Api
# Example:
# Seed__Demo=true Seed__DemoPassword=Demo123! dotnet run --project src/VoxMentor.Api -- --seed-demo
```

**What this creates:**

| Entity | Count | Details |
|---|---|---|
| Users | 2 | `demo-student@voxmentor.dev` (Student) · `demo-admin@voxmentor.dev` (ContentAdmin) |
| JobDescriptions | 2 | Amazon SDE II + Google L4, with 5 skill weights each |
| StudentMastery | 10 | Varied BKT probabilities (0.48–0.92) across existing DSA concepts |
| MockInterviews | 2 | 1 Technical (Amazon JD, score 82) + 1 Behavioral (score 76), both Completed with full TranscriptJson + ReviewJson |
| TutorSessions | 3 | Completed Q&A on Variables, Time Complexity, Linked Lists |

**Accounts** (password from `Seed:DemoPassword` — required when seeding):
- **demo-student@voxmentor.dev** — lands on a populated dashboard immediately
- **demo-admin@voxmentor.dev** — can access textbook upload and admin endpoints (`ContentAdmin`)

**Re-run safely:** idempotent — inserts only what's missing by deterministic ID or natural key. Re-running does not revert values you've changed (e.g. edited mastery probabilities); it only avoids duplicates.

**Production safety:** the seeder refuses to run unless `Seed:Demo=true` **and** the app is running in Development (`ASPNETCORE_ENVIRONMENT=Development`). Default configs have `Seed:Demo=false` — prod boots clean even if the flag leaks into env vars.

---

## Step 5: Run the Backend

```bash
# Terminal 1 — Run the main API
dotnet run --project src/VoxMentor.Api

# Expected output:
# info: VoxMentor.Api[0]
#   Now listening on: http://localhost:5000
# info: VoxMentor.Api[0]
#   Application started. Press Ctrl+C to shut down.

# Verify API is running
curl http://localhost:5000/health
# Expected: {"status":"Healthy","checks":{"postgres":"Healthy","redis":"Healthy","ollama":"Healthy"}}

# Open Swagger UI
# Browser → http://localhost:5000/swagger
```

### Optional: Run Microservices Separately

```bash
# Terminal 2 — AI Tutor Service
dotnet run --project src/VoxMentor.TutorService
# Listens on http://localhost:5001

# Terminal 3 — Code Execution Service
dotnet run --project src/VoxMentor.CodeExecService
# Listens on http://localhost:5002

# Terminal 4 — YARP Gateway
dotnet run --project src/VoxMentor.Gateway
# Listens on http://localhost:8080
```

---

## Step 6: Run the Frontend

```bash
# Terminal 5 — Run Next.js
cd web
npm install  # first time only
npm run dev

# Expected output:
# ▲ Next.js 16
# - Local: http://localhost:3000
# ✓ Ready in 2.3s

# Open browser
# http://localhost:3000
```

---

## Step 7: Run the Voice Service (For Mock Interviews)

```bash
# Terminal 6 — Run voice service
cd voice-service
python3 -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate
pip install -r requirements.txt
python3 main.py

# Listens on http://localhost:8001
```

---

## Step 8: Verify Everything Works

```bash
# 1. API health check
curl http://localhost:5000/health
# → {"status":"Healthy"}

# 2. Register a user
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"test@voxmentor.ai","password":"Test123!","fullName":"Test User"}'

# 3. Login
curl -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@voxmentor.ai","password":"Test123!"}'
# → Returns JWT token

# 4. Upload a JD
curl -X POST http://localhost:5000/api/v1/jd/upload \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  -d '{"rawText":"Amazon SDE-1: Strong DSA, DP, Graphs, System Design, OOP"}'
# → Returns parsed JD with skills + weights

# 5. Get Readiness Score
curl http://localhost:5000/api/v1/student/readiness \
  -H "Authorization: Bearer <your-token>"
# → {"readinessScore": 42, "gaps": [...]}

# 6. Get next adaptive question
curl http://localhost:5000/api/v1/student/next-question \
  -H "Authorization: Bearer <your-token>"
# → Returns a question at your mastery boundary

# 7. Open the frontend
# Browser → http://localhost:3000
# Login with test@voxmentor.ai / Test123!
# See the dashboard with Readiness Score
```

---

## Troubleshooting

### Docker containers won't start

```bash
# Check Docker is running
docker info

# Check available ports
lsof -i :6379  # Redis
lsof -i :11434 # Ollama

# If port is in use, kill the process:
kill -9 <PID>

# Restart all containers
docker compose down
docker compose up -d
```

### Ollama is slow / not responding

```bash
# Check Ollama is running
docker compose exec ollama ollama list

# Test Ollama directly
docker compose exec ollama ollama run llama3.2:3b "Hello"

# Check Ollama logs
docker compose logs ollama

# Restart Ollama
docker compose restart ollama
```

### Database migration fails

```bash
# Connect to Supabase (requires psql)
psql "postgres://postgres:<supabase-password>@<supabase-host>:5432/postgres?sslmode=require"

# Check tables
\dt

# Re-run migrations
dotnet ef database update --project src/VoxMentor.Infrastructure --startup-project src/VoxMentor.Api

# Re-seed data
psql "postgres://postgres:<supabase-password>@<supabase-host>:5432/postgres?sslmode=require" -f scripts/seed-dsa-concepts.sql
psql "postgres://postgres:<supabase-password>@<supabase-host>:5432/postgres?sslmode=require" -f scripts/seed-questions.sql

# Re-run demo seed (idempotent, Development only)
dotnet run --project src/VoxMentor.Api -- --seed-demo
```

### Frontend can't connect to backend

```bash
# Check .env.local in web/ folder
# NEXT_PUBLIC_API_URL=http://localhost:5000
# NEXT_PUBLIC_SIGNALR_URL=http://localhost:5000/hubs

# If using Gateway:
# NEXT_PUBLIC_API_URL=http://localhost:8080
# NEXT_PUBLIC_SIGNALR_URL=http://localhost:8080/hubs

# Restart Next.js
cd web
rm -rf .next
npm run dev
```

### Judge0 not executing code

Judge0 is **opt-in** in `docker-compose.yml`. The local stack ships its own
PostgreSQL (`judge0-db`) because Judge0 cannot share the API's Supabase
database.

```bash
# 1. Uncomment the judge0-db, judge0 and judge0-worker services in docker-compose.yml
# 2. In .env set JUDGE0_SECRET_KEY (random 32+ chars) and JUDGE0_DB_PASSWORD
# 3. Start the stack (judge0-db is started first as a dependency)
docker compose up -d judge0-db judge0 judge0-worker
```

No local database? Skip the compose stack and point **VoxMentor.CodeExecService**
at a remote Judge0 instead — it reads `Judge0:BaseUrl` (the API does not). See
the note below.

If Judge0 is running (locally or remote), verify:

```bash
# Check Judge0 is running (adjust URL if using remote endpoint)
curl http://localhost:2358/system_info
# → Should return JSON with version info

# Check Judge0 logs
docker compose logs judge0

# Test with a simple Python program
curl -X POST http://localhost:2358/submissions \
  -H "Content-Type: application/json" \
  -d '{"source_code":"print(\"hello\")","language_id":"71"}'
# → Should return a submission ID

# Get the result
curl http://localhost:2358/submissions/<submission-id>
# → Should show stdout: "hello"
```

**Using a remote Judge0 endpoint:** Set `Judge0:BaseUrl` in **VoxMentor.CodeExecService**'s
configuration (its `appsettings.Development.json`, user secrets, or the
`Judge0__BaseUrl` environment variable) to point at your Judge0 instance. The API
never reads this setting — CodeExec is the only caller.

---

## Development Workflow

```bash
# 1. Pull latest
git checkout main
git pull origin main

# 2. Create feature branch
git checkout -b feat/your-feature

# 3. Code + commit
git add .
git commit -m "feat: description of what you did"

# 4. Push
git push origin feat/your-feature

# 5. Create PR on GitHub
# 6. Get review from teammate
# 7. Merge to main
# 8. Pull latest
git checkout main
git pull origin main
```

---

## Ports Reference

| Service | Port | URL |
|---|---|---|
| Next.js Frontend | 3000 | http://localhost:3000 |
| Core API | 5000 | http://localhost:5000 |
| AI Tutor Service | 5001 | http://localhost:5001 |
| Code Exec Service | 5002 | http://localhost:5002 |
| YARP Gateway | 8080 | http://localhost:8080 |
| Seq (Logs) | 8081 | http://localhost:8081 |
| Redis | 6379 | localhost:6379 |
| Ollama | 11434 | http://localhost:11434 |
| Voice Service | 8001 | http://localhost:8001 |
| OTEL Collector (gRPC) | 4317 | localhost:4317 |
| OTEL Collector (HTTP) | 4318 | http://localhost:4318 |

> **Opt-in services (uncomment in docker-compose.yml):**
> | Service | Port | URL |
> |---|---|---|
> | Judge0 | 2358 | http://localhost:2358 |

> **Database:** PostgreSQL is hosted on Supabase (cloud); no local PostgreSQL
> port by default. The optional Judge0 stack's `judge0-db` stays on the compose
> network only and publishes no port.

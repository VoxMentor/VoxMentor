# VoxMentor — Week 1 Plan

**Team:** 2 members · **Timeline:** 7 days

## Goal

Go from "docs only" to a working skeleton:

- Infrastructure up via `docker compose up`
- User can register and log in
- Database seeded (50 DSA concepts + prerequisites)
- Code running end-to-end (backend :5000, frontend :3000)
- CI green on GitHub

This maps to **v1.1.0** in `docs/CHANGELOG.md`.

## Non-Goals (Week 1)

Scope discipline — these are weeks 2-5, NOT this week:

- AI Tutor / RAG Coach
- Code Execution (Judge0 integration)
- Voice (Whisper + Piper)
- Mock interview engine
- Resume analyzer

## Role Split

| | Member A — Backend (.NET) | Member B — Frontend + Infra |
|---|---|---|
| **D1** | Scaffold .NET solution (7 projects), `Directory.Build.props`, `Directory.Packages.props`, Domain entities/enums | Scaffold `web/` Next.js app, base layout + routing, `.env.local` |
| **Both** | Write `docker-compose.yml` (postgres, redis, ollama, judge0, seq), bring it up. **Start Ollama model pull (~2.3GB) in background now.** | |
| **D2** | EF Core + PostgreSQL + pgvector, schema for all tables + first migration | Auth UI: register + login pages, API client, JWT storage, protected routes |
| **D3** | ASP.NET Identity + JWT (register/login/refresh), `/health` with Postgres/Redis/Ollama checks, Serilog | Wire frontend to `/health` + auth endpoints → **register/login works end-to-end** |
| **D4** | Knowledge graph (recursive CTE), seed 50 DSA concepts + prerequisite edges | Dashboard skeleton (static concept list), SignalR hub scaffold, GitHub Actions CI (build + test) |
| **D5** | YARP gateway (:8080 routing), Seq log ingestion, Hangfire skeleton, final `/health` polish | CI on every PR + branch protection enforced, verify full stack from :3000, tag `v1.1.0` |
| **D6-7** | Buffer: fix issues, review PRs, merge, short demo | |

## Database Schema (create on D2)

Tables: `AspNetUsers`, `Concepts`, `Prerequisites`, `Questions`, `StudentMastery`, `TextbookChunks`, `CodeSubmissions`, `MockInterviews`, `AuditLogs`

## Environment Checklist (Day 0)

Verify on both machines:

- Git (latest)
- VS Code + C# Dev Kit
- Docker Desktop (running)
- .NET 8 SDK — `dotnet --version` → 8.0.x
- Node.js 20 — `node --version` → v20.x
- Python 3.12+ — `python --version`

## Git Workflow

1. `main` protected (PR + 1 approval)
2. `feat/<name>` → PR → `main`
3. Commit style: `feat:`, `fix:`, `refactor:` prefix

## Day 0 Tasks

- [ ] Add member to GitHub repo
- [ ] Push `docs/` folder to repo
- [ ] Protect `main` branch
- [ ] Both machines: install + verify prerequisites
- [ ] Read `autosetup/VoxMentor-Beginner-Friendly-Guide-v1.0.md` and `docs/ARCHITECTURE.md`

## End-of-Week Deliverable

- [ ] `docker compose up` brings up all infra (postgres, redis, ollama, judge0, seq)
- [ ] Ollama models pulled (`llama3.2:3b`, `nomic-embed-text`)
- [ ] Register → login works from browser at `:3000`
- [ ] `/health` shows Postgres + Redis + Ollama healthy
- [ ] DB has 50 concepts + prerequisite edges
- [ ] CI green on every PR
- [ ] Tag `v1.1.0`
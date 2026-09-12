# VoxMentor Documentation

AI-powered interview preparation platform for college students. Paste a job description, practice DSA problems, take mock interviews with voice, and get a readiness score.

## What VoxMentor Does

1. **Upload a Job Description** → AI parses it into weighted skills (DP: 35%, Graphs: 25%, etc.)
2. **Practice DSA Problems** → Adaptive questions based on your mastery gaps + JD priorities
3. **Get Evaluated** → AI scores correctness, complexity, style; checks plagiarism
4. **Track Mastery** → Bayesian Knowledge Tracing updates after every answer
5. **Mock Interviews** → AI interviewer simulates real company interviews (voice + code)
6. **Resume Check** → ATS score + keyword gap analysis against the JD

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Next.js 16 + React 19 + Tailwind CSS + Monaco Editor |
| Backend | .NET 8 (Clean Architecture, CQRS with MediatR) |
| Database | PostgreSQL 15 + pgvector (relational + graph + vector in one DB) |
| Cache / Events | Redis 7 (cache + SignalR backplane + Streams) |
| AI (LLM) | Ollama (Llama 3.2 3B local, zero cost) |
| AI (Embeddings) | Ollama nomic-embed-text (768-dim) |
| Code Execution | Judge0 (sandboxed, 60+ languages) |
| Voice | faster-whisper (STT) + Piper (TTS) |
| Gateway | YARP (.NET reverse proxy) |
| Logging | Serilog → Seq |
| CI/CD | GitHub Actions |
| Background Jobs | Hangfire (BKT tuning + spaced repetition) |

## Architecture

Modular monolith + 3 microservices + YARP API Gateway:

```text
Client (Next.js :3000)
    ↓ HTTPS + SignalR + WebRTC
YARP Gateway (:8080)
    ↓ routes to
Core API (:5000) ←→ Tutor Service (:5001) ←→ CodeExec Service (:5002) ←→ Voice Service (:8001)
    ↓                                                                        ↓
PostgreSQL + pgvector ←→ Redis 7 ←→ Ollama (local LLM)
```

- **Core API** — Auth, BKT, adaptive selection, knowledge graph, mock engine, resume (fast: 50-200ms)
- **Tutor Service** — RAG AI coach with streaming (slow: 3-8s, isolated LLM crashes)
- **CodeExec Service** — Judge0 code execution + AI eval + plagiarism (slow: 3-6s, untrusted code)
- **Voice Service** (Python) — Whisper STT + Piper TTS (CPU-heavy, 2GB RAM)

See `ARCHITECTURE.md` for full architecture decisions (ADRs).

## Project Structure

```text
VoxMentor/
├── src/
│   ├── VoxMentor.Domain/          ← Pure C# entities, value objects, enums (zero deps)
│   ├── VoxMentor.Application/     ← CQRS commands/queries, BKT engine, interfaces
│   ├── VoxMentor.Infrastructure/  ← EF Core, Redis, Ollama client, JWT
│   ├── VoxMentor.Api/             ← Main API (controllers, middleware, Swagger)
│   ├── VoxMentor.TutorService/    ← AI Tutor microservice
│   ├── VoxMentor.CodeExecService/ ← Code Execution microservice
│   ├── VoxMentor.Gateway/         ← YARP API Gateway
│   └── VoxMentor.Tests/           ← Unit + integration tests
├── web/                           ← Next.js 16 frontend (React 19, Tailwind, Monaco)
├── voice-service/                 ← Python FastAPI (Whisper + Piper)
├── docker/                        ← Dockerfiles per service
├── scripts/                       ← DB seed scripts (SQL + Python)
├── docs/                          ← This folder
├── docker-compose.yml             ← One command runs everything
└── VoxMentor.slnx                 ← .NET solution file
```

## Documentation Index

| Doc | Purpose |
|---|---|
| **BEGINNER-GUIDE.md** | Start here — what VoxMentor is and how it works (non-technical overview) |
| **PROJECT-STRUCTURE.md** | Where every folder and file lives |
| **SETUP.md** | How to run the project locally (step-by-step) |
| **ARCHITECTURE.md** | Deep technical design + 10 architecture decisions (ADRs) |
| **API.md** | All 31 REST endpoints + 3 SignalR hubs (reference) |
| **WEEK1-PLAN.md** | Week 1 task plan (scaffold + auth + DB) |
| **WEEK2-PLAN.md** | Week 2 task plan (backend endpoints: practice + admin API) |
| **WEEK3-PLAN.md** | Week 3 task plan (AI Tutor RAG + voice scaffold + deferred dashboard UI) |
| **CHANGELOG.md** | What changed each version (v1.0.0 → v1.5.0) |

## Development Timeline

| Week | Version | Focus |
|---|---|---|
| Week 1 | v1.1.0 | Scaffold, auth, DB, Docker, CI |
| **Week 2** | **v1.2.0** | **BKT engine, code execution, AI eval, plagiarism** |
| Week 3 | v1.3.0 | AI Tutor (RAG), streaming, voice service scaffold |
| Week 4 | v1.4.0 | JD Intelligence, mock interview engine, adaptive selector |
| Week 5 | v1.5.0 | Voice integration, resume ATS, company-specific tracks |
| Week 6 | v2.0.0 | Production deploy, load testing, demo polish |

## Quick Start

```bash
# 1. Start infrastructure
docker compose up -d

# 2. Pull Ollama models (one-time, ~2GB)
docker compose exec ollama ollama pull llama3.2:3b
docker compose exec ollama ollama pull nomic-embed-text

# 3. Initialize database
bash scripts/init-db.sh

# 4. Run backend
dotnet run --project src/VoxMentor.Api

# 5. Run frontend
cd web && npm install && npm run dev

# 6. Open http://localhost:3000
```

See `SETUP.md` for full setup instructions and troubleshooting.

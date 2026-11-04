# VoxMentor

AI-powered interview preparation platform for college students. Paste a job description, practice DSA problems, take mock interviews with voice, and get a readiness score.

[![Build](https://img.shields.io/github/actions/workflow/status/VoxMentor/VoxMentor/ci.yml?branch=main&label=build)](https://github.com/VoxMentor/VoxMentor/actions)
[![License](https://img.shields.io/github/license/VoxMentor/VoxMentor)](https://github.com/VoxMentor/VoxMentor/blob/main/LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD8?logo=dotnet)](https://dotnet.microsoft.com/)

## Hero

| Dashboard | Practice / Code Editor | AI Tutor |
|---|---|---|
| ![Dashboard](docs/screenshots/dashboard.png) | ![Practice / Code Editor](docs/screenshots/practice.png) | ![AI Tutor](docs/screenshots/tutor.png) |
| *(screenshot to be added)* | *(screenshot to be added)* | *(screenshot to be added)* |

| JD Gap Analysis | Mock Interview |
|---|---|
| ![JD Gap Analysis](docs/screenshots/jd-gap-analysis.png) | ![Mock Interview](docs/screenshots/mock-interview.png) |
| *(screenshot to be added)* | *(screenshot to be added)* |

> Screenshots live under `docs/screenshots/` and are to be added. No image files are committed yet.

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
| Database | PostgreSQL 15 + pgvector |
| Cache / Events | Redis 7 |
| AI (LLM) | Ollama (Llama 3.2 3B, local) |
| AI (Embeddings) | Ollama nomic-embed-text |
| Code Execution | Judge0 (sandboxed, 60+ languages) |
| Voice | faster-whisper (STT) + Piper (TTS) |
| Gateway | YARP (.NET reverse proxy) |
| CI/CD | GitHub Actions |

## Architecture

Modular monolith (.NET 8 Core API) + 3 microservices (AI Tutor, Code Execution, Voice) + YARP API Gateway, all backed by one PostgreSQL + pgvector database.

See the full diagram and ADRs in **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

## Project Structure

```
VoxMentor/
├── src/                       ← .NET 8 solution (Clean Architecture)
│   ├── VoxMentor.Domain/      ← Pure entities, value objects, enums
│   ├── VoxMentor.Application/ ← CQRS commands/queries, BKT engine
│   ├── VoxMentor.Infrastructure/ ← EF Core, Redis, Ollama client, JWT
│   ├── VoxMentor.Api/         ← Main API (controllers, Swagger)
│   ├── VoxMentor.TutorService/ ← AI Tutor microservice
│   ├── VoxMentor.CodeExecService/ ← Code execution microservice
│   ├── VoxMentor.Gateway/     ← YARP API Gateway
│   └── VoxMentor.Tests/       ← Unit + integration tests
├── web/                       ← Next.js 16 frontend (React 19, Tailwind, Monaco)
├── voice-service/             ← Python FastAPI (Whisper + Piper)
├── docker/                    ← Dockerfiles per service
├── scripts/                   ← DB seed + RAG corpus scripts
├── docs/                      ← Documentation
├── docker-compose.yml         ← One command runs the whole stack
└── VoxMentor.slnx             ← .NET solution file
```

## Documentation Index

| Doc | Purpose |
|---|---|
| [BEGINNER-GUIDE.md](docs/BEGINNER-GUIDE.md) | Start here — what VoxMentor is and how it works (non-technical overview) |
| [PROJECT-STRUCTURE.md](docs/PROJECT-STRUCTURE.md) | Where every folder and file lives |
| [SETUP.md](docs/SETUP.md) | Step-by-step local setup and troubleshooting |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Deep technical design + architecture decisions (ADRs) |
| [API.md](docs/API.md) | All REST endpoints + SignalR hubs (reference) |
| [CHANGELOG.md](docs/CHANGELOG.md) | What changed each version |
| [WEEK1-PLAN.md](docs/WEEK1-PLAN.md) | Week 1 task plan (scaffold + auth + DB) |
| [WEEK2-PLAN.md](docs/WEEK2-PLAN.md) | Week 2 task plan (backend endpoints: practice + admin API) |
| [WEEK3-PLAN.md](docs/WEEK3-PLAN.md) | Week 3 task plan (AI Tutor RAG + voice scaffold) |

## Quick Start

### Prerequisites

| Tool | Version | Why |
|---|---|---|
| Docker Desktop | Latest | Runs Postgres, Redis, Ollama, Judge0, Seq |
| .NET 8 SDK | 8.0.x | Backend |
| Node.js | 20.x | Frontend (Next.js 16) |
| Python | 3.12+ | Voice service |
| Git | Latest | Version control |

### One-command stack up

```bash
# 1. Start all infrastructure (Postgres, Redis, Ollama, Judge0, Seq, ...)
docker compose up -d

# 2. Pull Ollama models (one-time, ~2GB)
docker compose exec ollama ollama pull llama3.2:3b

# 3. Initialize the database (migrations + seed data)
bash scripts/init-db.sh
```

### First use

```bash
# 4. Run the backend (Terminal 1)
dotnet run --project src/VoxMentor.Api

# 5. Run the frontend (Terminal 2)
cd web && npm install && npm run dev

# 6. Open http://localhost:3000
```

See [SETUP.md](docs/SETUP.md) for full setup instructions, the voice service, and troubleshooting.

## Development Timeline

| Week | Version | Focus |
|---|---|---|
| Week 1 | v1.1.0 | Scaffold, auth, DB, Docker, CI |
| Week 2 | v1.2.0 | BKT engine, code execution, AI eval, plagiarism |
| Week 3 | v1.3.0 | AI Tutor (RAG), streaming, voice service scaffold |
| Week 4 | v1.4.0 | JD Intelligence, mock interview engine, adaptive selector |
| Week 5 | v1.5.0 | Voice integration, resume ATS, company-specific tracks |
| Week 6 | v2.0.0 | Production deploy, load testing, demo polish |

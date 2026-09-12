# Changelog

All notable changes to VoxMentor will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Planned for Week 6
- Docker Compose production configuration
- GitHub Actions CI/CD pipeline
- Load testing with NBomber
- OpenTelemetry distributed tracing
- Demo seed data
- README polish + screenshots

---

## [1.5.0] — 2026-08-XX (Week 5)

### Added
- Resume ATS Analyzer (pdfplumber + keyword matching)
- Voice integration (faster-whisper STT + Piper TTS)
- Company-specific interview tracks (Amazon LPs, Google Googliness)
- Rate limiting for AI Coach (10 queries/hour/student)
- Mock Interview Review job (post-interview personalized feedback)

### Changed
- Mock Interview Engine now supports voice mode via WebRTC
- Readiness Score recalculates after each mock interview
- Spaced repetition half-life tuned to 14 days (was 30)

### Fixed
- Ollama streaming sometimes truncated on long responses
- Judge0 timeout was 5s, increased to 10s for complex problems

---

## [1.4.0] — 2026-08-XX (Week 4)

### Added
- JD Intelligence Engine (Ollama parses JD → structured JSON)
- Readiness Score calculator (weighted mastery × JD weights)
- Isomorphic Question Generation (parameterized templates)
- AI-Human Difficulty Calibration (5-factor rubric)
- Adaptive Selector with JD-weighted epsilon-greedy
- Redis caching for concept + mastery + JD params

### Changed
- BKT engine now uses JD-weighted topic priorities
- Knowledge Graph queries now support "almost eligible" analysis

---

## [1.3.0] — 2026-08-XX (Week 3)

### Added
- AI Tutor Service (microservice, RAG pipeline)
- RAG AI Coach with streaming responses via SignalR
- pgvector IVFFlat index for textbook chunk search
- Polly circuit breaker for AI Tutor Service
- Textbook content embedded (CTCI + GFG articles)
- Voice Service scaffold (Python FastAPI + Whisper + Piper)
- Progress tracking UI (mastery bars, completion %) — deferred from Week 2
- Dashboard mastery heatmap + recent activity — deferred from Week 2

### Changed
- Vector search now uses pgvector cosine similarity operator (`<=>`)
- SignalR hub restructured for multi-service callback pattern

---

## [1.2.0] — 2026-08-XX (Week 2)

### Added
- BKT Engine (pure C#, 4-parameter Bayesian Knowledge Tracing)
- SubmitAnswerHandler (CQRS command, loads mastery → BKT update → persist → emit event)
- POST /api/v1/answer (AnswerController, Student role)
- POST /api/v1/execute (Judge0 sandboxed execution)
- POST /api/v1/student/submit-code (Judge0 → persist → AI eval → plagiarism → BKT pipeline)
- GET /api/v1/questions, GET by id (hidden cases excluded), adaptive GET /api/v1/student/next-question
- GET /api/v1/student/mastery + GET readiness (JD-weighted score, gaps)
- Admin question-bank CRUD + 100 practice questions seeded
- Knowledge-graph queries served over HTTP (prerequisites, eligible, almost-eligible)
- AI Code Evaluation (Ollama analyzes correctness, complexity, style, edge cases)
- Plagiarism Detection (CodeBERT embeddings + AST comparison via tree-sitter)
- Per-submission idempotency (MasteryApplied claim)
- Hangfire nightly BKT parameter tuning job (EM algorithm)
- Hangfire nightly spaced repetition decay job

### Changed
- StudentMastery table now includes LastPracticedAt for spaced repetition
- CodeSubmissions table now includes PlagiarismScore column
- EF Core global query filter for multi-tenant isolation

---

## [1.1.0] — 2026-08-XX (Week 1)

### Added
- Project structure (.NET 8 solution with 7 projects)
- Clean Architecture setup (Domain → Application → Infrastructure → Api)
- ASP.NET Identity + JWT authentication (register, login, refresh token rotation)
- EF Core with PostgreSQL + pgvector
- Database schema (AspNetUsers, Concepts, Prerequisites, Questions, StudentMastery, TextbookChunks, CodeSubmissions, MockInterviews, AuditLogs)
- 50 DSA concepts seeded (Arrays → DP → Graphs → Trees → System Design)
- Knowledge Graph with recursive CTE prerequisite queries
- YARP API Gateway configuration
- Docker Compose with PostgreSQL, Redis, Ollama, Judge0, Seq
- GitHub Actions CI pipeline (build + test on every PR)
- Branch protection rules on main (PR required + 1 approval)
- .gitignore, Directory.Build.props, Directory.Packages.props

### Infrastructure
- docker-compose.yml with 8 services (postgres, redis, ollama, judge0, seq, voice, api, web)
- Health check endpoint (/health)
- Serilog structured logging with RequestId
- Seq log ingestion

---

## [1.0.0] — 2026-08-XX (Initial)

### Added
- Repository created
- README.md with project overview
- MIT License
- Initial commit

---

## Versioning Rules

| Version Part | When to Bump | Example |
|---|---|---|
| **Major** (X.0.0) | Breaking change to API contract | 1.0.0 → 2.0.0 (API v2) |
| **Minor** (1.X.0) | New feature, no breaking changes | 1.0.0 → 1.1.0 (added BKT) |
| **Patch** (1.0.X) | Bug fix, no new features | 1.0.0 → 1.0.1 (fixed Ollama timeout) |

## How to Tag a Release

```bash
# After merging a week's worth of PRs to main:
git checkout main
git pull origin main

# Create a tag
git tag -a v1.1.0 -m "Week 2: BKT engine, answer submission, code execution"

# Push the tag
git push origin v1.1.0

# GitHub will show it under Releases
```

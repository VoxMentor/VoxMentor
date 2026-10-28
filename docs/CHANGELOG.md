# Changelog

All notable changes to VoxMentor will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- Mock interview REST complete + review + history ([#125](https://github.com/VoxMentor/VoxMentor/issues/125)): `POST /api/v1/mock/{interviewId}/complete` runs under the same per-interview lock as answer, captures the JD-weighted readiness score before/after, asks Ollama for the per-problem review (JSON-schema `format`, `temperature` 0; degrades to a stored canned review — score 50, no mastery writes — when unreachable), applies BKT mastery updates from the review's `solved` verdicts (questions map to transcript `problemN` entries by order), then flips `Status` to `Completed` with `Score`, `ReviewJson`, `Feedback`, and `CompletedAt` (404 unknown/foreign, 409 already ended); `GET /api/v1/mock/{interviewId}/review` returns the stored review (404 unless the interview is `Completed` with a review); `GET /api/v1/mock/history` lists only the caller's interviews newest first with `page`/`pageSize` paging (defaults 1/20, clamped 1–100) and `score` null until `Completed`
- Mock interview REST start + answer ([#124](https://github.com/VoxMentor/VoxMentor/issues/124)): `POST /api/v1/mock/start` picks the opening problem through the practice selector (now accepting an optional `JdId` to pin the JD weights), has Ollama generate the opening line from the JD's company/role (degrades to a canned line when Ollama is unreachable), enforces a per-student daily quota (`MockInterview:DailyLimit`, default 1/day → `429` with `Retry-After`), and persists an `InProgress` interview whose `TranscriptJson` starts with the interviewer turn; `POST /api/v1/mock/{interviewId}/answer` appends the candidate turn plus the Ollama follow-up probe (order preserved, `turnNumber` = total turns), 404 for unknown/foreign interviews (global `IUserOwned` filter), 409 once ended, 400 on an empty message
- AI Tutor background answer generation ([#72](https://github.com/VoxMentor/VoxMentor/issues/72)): `POST /api/v1/tutor/ask` enqueues a Hangfire job that atomically claims the Pending session (`TutorSessions.Status` concurrency token — no global job lock) and persists answer + token count (Completed only after the final `done=true` chunk) or a stored error message (Failed — new `TutorSessions.Error` column); failure-count circuit breaker on tutor generation (5 failures in 30s → open 30s → ask returns 503) records full-stream outcomes (HTTP, body read, and NDJSON failures); minute-level reconciliation sweep re-queues lost Pending sessions behind a `QueuedAt` enqueue claim and fails Streaming sessions stale past their `StartedAt` (migration `AddTutorSessionClaimTimestamps`)
- MockInterview contract columns ([#123](https://github.com/VoxMentor/VoxMentor/issues/123)): `MockInterviews` gains nullable `JdId` (FK → `JobDescriptions`, `SetNull` on delete so interview history outlives its JD), `Status` (new `MockInterviewStatus` enum stored as string(20), mirroring the tutor-session pattern, DB default `Pending`), `TranscriptJson` (ordered turns), and `ReviewJson` (structured AI review); `Type` stays on the existing `InterviewType` enum — no duplicate string column (migration `AddMockInterviewJdStatusTranscriptReview`)

### Changed
- TutorHub `AskTutor` links host shutdown into its cancellation token ([#90](https://github.com/VoxMentor/VoxMentor/issues/90)): `IHostApplicationLifetime.ApplicationStopping` is combined with `Context.ConnectionAborted`, so a graceful shutdown cancels in-flight streams immediately and marks their sessions Failed while the DbContext is still alive, instead of waiting out Kestrel's 30s connection-drain timeout

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

## [1.3.0] — 2026-10-12 (Week 3)

### Added
- AI Tutor Service (microservice, RAG pipeline)
- RAG AI Coach with streaming responses via SignalR
- pgvector HNSW index for textbook chunk search
- Polly circuit breaker for AI Tutor Service
- Textbook content embedded (CTCI + GFG articles)
- Voice Service scaffold (Python FastAPI + Whisper + Piper)
- Progress tracking UI (mastery bars, completion %) — deferred from Week 2
- Dashboard mastery heatmap + recent activity — deferred from Week 2
- Seeded RAG corpus ([#75](https://github.com/VoxMentor/VoxMentor/issues/75)): `scripts/seed-corpus.py` fetches 88 curated GeeksforGeeks DSA articles (`scripts/corpus/articles.json`, one list per concept), groups them per concept and uploads one file per concept through `POST /api/v1/admin/textbook/upload` (~680 chunks, >= 5 per concept); `plan`/`seed`/`verify` modes, `--clean` re-seed, keyword + stdlib TF-IDF auto-mapping for local files in `scripts/corpus/source/`, offline tests in `scripts/test_seed_corpus.py` now enforced by a `scripts` CI job
- Tutor retrieval + prompt evaluation harness ([#77](https://github.com/VoxMentor/VoxMentor/issues/77)): `scripts/eval-tutor.py` seeds a tagged eval corpus and scores 20 golden Q&As (recall@k/MRR sweep at k=3/5/7, citation precision, keyphrase coverage, latency) with pass/fail gates; baseline snapshot in `scripts/eval/baseline.md`
- Hangfire nightly BKT parameter tuning job (EM algorithm) ([#57](https://github.com/VoxMentor/VoxMentor/issues/57))
- Hangfire nightly spaced repetition decay job ([#57](https://github.com/VoxMentor/VoxMentor/issues/57))

### Changed
- Vector search now uses pgvector cosine similarity operator (`<=>`)
- SignalR hub restructured for multi-service callback pattern
- Tutor prompt moved to single-source `prompts/tutor-prompt.txt` (embedded by C#, rendered by the eval): numbered `[n]` citations, refuse-to-guess instruction ([#77](https://github.com/VoxMentor/VoxMentor/issues/77))
- Tutor retrieval: `Rag:TopK` config (default 5, replaces hardcoded `LIMIT 5`); per-query metrics logged (k, concept, chunk cosine distances, elapsed ms) ([#77](https://github.com/VoxMentor/VoxMentor/issues/77))

### Fixed
- Tutor chunk retrieval returned 0–1 rows: the IVFFlat embedding index on a small `TextbookChunks` table probes one mostly-empty list under `LIMIT` plans — replaced with HNSW (same as `IX_CodeSubmissions_CodeEmbedding`), migration `SwitchTextbookEmbeddingIndexToHnsw` ([#77](https://github.com/VoxMentor/VoxMentor/issues/77))

---

## [1.2.0] — 2026-09-29 (Week 2)

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
- Multi-admin RBAC ([#82](https://github.com/VoxMentor/VoxMentor/issues/82)): seed `SuperAdmin`, `ContentAdmin`, `PlatformAdmin`; named policies `ManageContent` / `ManagePlatform` / `ManageRoles`; SuperAdmin-only role management; Hangfire/ops + audit gated to PlatformAdmin

### Changed
- StudentMastery table now includes LastPracticedAt for spaced repetition
- CodeSubmissions table now includes PlagiarismScore column
- EF Core global query filter for multi-tenant isolation

---

## [1.1.0] — 2026-09-02 (Week 1)

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

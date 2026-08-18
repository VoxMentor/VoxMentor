# VoxMentor — Architecture Documentation

## Overview

VoxMentor uses a **modular monolith + 3 microservices + YARP API Gateway** architecture.

- **Main App (Monolith)**: Handles everything fast (sub-200ms) — auth, BKT, adaptive selection, knowledge graph, mock interview engine, resume analyzer
- **3 Microservices**: Handle everything slow (3-15s) or needs isolation — AI Tutor (RAG), Code Execution (Judge0), Voice (Whisper+Piper)
- **Shared Database**: One PostgreSQL + pgvector for all services

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                  CLIENT TIER (Next.js 14 + Monaco Editor)            │
│   Dashboard · Practice · Mock Interview (voice+code) · Resume      │
└──────────┬──────────────────────────────────────────────────────────┘
           │ HTTPS + SignalR (WebSocket) + WebRTC (voice)
┌──────────▼──────────────────────────────────────────────────────────┐
│              YARP API GATEWAY (.NET 8)                              │
│  Auth (JWT) · Rate Limiting · CORS · Routing · Health Checks        │
└──────┬──────────┬──────────────────┬────────────────────────────────┘
       │          │                  │
       ▼          ▼                  ▼
┌──────────┐ ┌──────────┐    ┌──────────────┐
│ CORE API │ │AI TUTOR  │    │CODE EXEC     │
│ (Mono)   │ │SERVICE   │    │SERVICE       │
│ .NET 8   │ │.NET 8    │    │.NET 8+Judge0 │
│          │ │          │    │              │
│ Fast:    │ │ Slow:    │    │ Slow:        │
│ 50-200ms │ │ 3-8s     │    │ 3-6s         │
│          │ │ (Ollama) │    │(Judge0+Ollama)│
│ Modules: │ │          │    │              │
│ - Auth   │ │ - RAG    │    │ - Run code   │
│ - BKT    │ │   Coach  │    │ - AI eval    │
│ - JD Eng │ │ - Stream │    │ - Plagiarism │
│ - Adapt  │ │   tokens │    │   (AST+BERT) │
│ - Graph  │ │ - Polly  │    │ - Polly      │
│ - Mock   │ │   breaker │    │   breaker    │
│   Engine │ │          │    │              │
│ - Resume │ │          │    │              │
│ - Ready  │ │          │    │              │
│   Score  │ │          │    │              │
│ - Spaced │ │          │    │              │
│   Rep    │ │          │    │              │
│ - Isomor │ │          │    │              │
│   Gen    │ │          │    │              │
│          │ │          │    │              │
│ Hangfire │ │          │    │              │
│ Redis    │ │          │    │              │
│ SignalR  │ │          │    │              │
│ EF Core  │ │          │    │              │
│ Audit    │ │          │    │              │
└────┬─────┘ └────┬─────┘    └──────┬───────┘
     │            │                  │
     │   ┌────────▼────────┐        │
     │   │ VOICE SERVICE   │        │
     │   │ (Python)        │        │
     │   │                 │        │
     │   │ Whisper STT     │        │
     │   │ Piper TTS       │        │
     │   │ WebRTC audio    │        │
     │   └────────┬────────┘        │
     │            │                  │
┌────▼────────────▼──────────────────▼──────────────────────────────┐
│                    REDIS 7 (3 ROLES)                               │
│  Cache (concept, mastery, embeddings) · SignalR Backplane ·       │
│  Redis Streams (Event Bus: MasteryUpdated, CodeSubmitted, etc.)    │
└───────────────────────────┬────────────────────────────────────────┘
                            │
┌───────────────────────────▼────────────────────────────────────────┐
│                POSTGRESQL 15 + pgvector                             │
│  Relational (users, JDs, mastery, submissions, interviews, audit)  │
│  Graph (concepts + prerequisites, recursive CTE)                    │
│  Vector (textbook chunks, CodeBERT embeddings, IVFFlat index)      │
│  Hangfire schema (job queue + state)                               │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│                OLLAMA (LOCAL LLM)                                   │
│  llama3.2:3b (chat, 2GB RAM, 3-8s/response)                       │
│  nomic-embed-text (embeddings, 768-dim, ~100ms/embed)             │
│  :11434 · Local · Free                                             │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│                OBSERVABILITY                                        │
│  Serilog → Seq (structured logs + RequestId)                        │
│  OpenTelemetry (distributed traces across all services)            │
│  Health checks: /health (Postgres + Redis + Ollama + Judge0)       │
└────────────────────────────────────────────────────────────────────┘
```

## Clean Architecture (Inside the Core API)

```
┌─────────────────────────────────────────────────────────────────┐
│                    VoxMentor.Api (Presentation)                 │
│  Controllers · SignalR Hubs · YARP Config · Swagger            │
│  Middleware: Exception Handler · RequestId · TenantFilter       │
└────────────────────────────┬────────────────────────────────────┘
                             │ depends on
┌────────────────────────────▼────────────────────────────────────┐
│                 VoxMentor.Application (Use Cases)              │
│                                                                │
│  CQRS (MediatR):                                               │
│  Commands: SubmitAnswer · UploadJd · StartMock · SubmitCode    │
│  Queries: GetReadiness · GetNextQuestion · GetMasteryProfile   │
│                                                                │
│  Services: BktEngine · JdEngine · AdaptiveSelector ·          │
│  ReadinessCalc · IsomorphicGen · SpacedRep · ResumeATS ·       │
│  PlagiarismSvc                                                  │
│                                                                │
│  Pipeline Behaviors: Validation · Logging · Performance · Retry│
│                                                                │
│  Interfaces (Ports):                                           │
│  IMasteryRepo · ITutorClient · ICodeExecClient · IEmbedClient  │
│  IConceptRepo · IJdRepo · IQuestionRepo · IVoiceClient         │
└────────────────────────────┬────────────────────────────────────┘
                             │ depends on
┌────────────────────────────▼────────────────────────────────────┐
│                   VoxMentor.Domain (Pure C#)                   │
│                                                                │
│  Entities: Student · Concept · Question · StudentMastery ·     │
│  JobDescription · CodeSubmission · MockInterview · AuditLog     │
│                                                                │
│  Value Objects: ReadinessScore · MasteryProbability ·          │
│  TopicWeight · DifficultyScore · InterviewState                │
│                                                                │
│  Domain Events: MasteryUpdated · CodeSubmitted ·               │
│  MockInterviewCompleted · PlagiarismDetected                   │
│                                                                │
│  Enums: QuestionType · InterviewState · MockType               │
│                                                                │
│  NO DEPENDENCIES — pure C#                                      │
└─────────────────────────────────────────────────────────────────┘
```

## Why 3 Microservices (and Not More)

| Service | Extracted? | Why |
|---|---|---|
| AI Tutor Service | YES | LLM calls (3-8s) + Ollama can crash + prompt changes daily |
| Code Execution Service | YES | Running untrusted code (dangerous) + CPU-heavy plagiarism + Judge0 can crash |
| Voice Service | YES | Python (different language) + CPU-heavy + 2GB RAM for Whisper |
| BKT Engine | NO | Pure math, <1ms, no I/O |
| JD Intelligence | NO | One-time Ollama call per JD upload. Not frequent. |
| Adaptive Selector | NO | Pure SQL + C#, <50ms |
| Knowledge Graph | NO | Recursive CTE queries, <10ms |
| Mock Interview Engine | NO | State machine is pure C#. CALLS the services (Tutor, Voice, CodeExec) for slow operations. |
| Resume ATS | NO | Lightweight pdfplumber call. Not worth a separate service. |

## Key Architecture Decisions (ADRs)

### ADR-001: Modular Monolith (Not Full Microservices)
**Decision**: Build a modular monolith with clean module boundaries. Extract only 3 services where genuine technical justification exists.
**Rationale**: 4-person team, 6-week timeline. Full microservices add 2 weeks of infrastructure overhead. In-process calls are 200x faster than HTTP.
**Tradeoff**: Tighter coupling, but pragmatic for team size.

### ADR-002: PostgreSQL + pgvector (Not Cosmos DB / Neo4j / Pinecone)
**Decision**: Use one PostgreSQL database with pgvector extension for all three data roles (documents, graph, vectors).
**Rationale**: Free, open-source. pgvector IVFFlat index is sub-50ms for 100K chunks. Recursive CTEs replace graph database for curriculum (100-500 concepts). No separate vector DB bill.
**Tradeoff**: No Cosmos change feed (use Postgres LISTEN/NOTIFY instead). No graph visualization (build custom UI).

### ADR-003: Ollama Llama 3.2 (Not Azure OpenAI / GPT-4)
**Decision**: Use Ollama hosting Llama 3.2 3B locally. Zero per-token cost.
**Rationale**: Free, local, no vendor lock-in, no network latency. 2GB RAM, 3-8s per response. Good enough for interview tutoring.
**Tradeoff**: Smaller model than GPT-4. Responses are good but not GPT-4 quality.

### ADR-004: YARP API Gateway (Not Nginx / Kong / No Gateway)
**Decision**: Use YARP (.NET native reverse proxy) as API gateway.
**Rationale**: Same language as backend. Same Docker image. Config in C#. Built-in health checks, rate limiting, routing. Zero learning curve.
**Tradeoff**: Less feature-rich than Kong, but sufficient for our needs.

### ADR-005: Redis Streams (Not RabbitMQ / Kafka)
**Decision**: Use Redis Streams for async cross-service events.
**Rationale**: Redis is already in the stack (cache + SignalR backplane). Streams are built-in. No separate message broker to run.
**Tradeoff**: Less powerful than Kafka for high-throughput scenarios, but we process ~100 events/min.

### ADR-006: Shared Database (Not Database-Per-Service)
**Decision**: All services share the same PostgreSQL database.
**Rationale**: Only 3 services. Data sync complexity not worth it for this scale.
**Tradeoff**: Tighter coupling, but pragmatic. Would reconsider at 5+ services.

### ADR-007: faster-whisper (Not Cloud STT)
**Decision**: Use faster-whisper locally for speech-to-text.
**Rationale**: Free, local, supports Indian languages. ~300ms for 10s utterance. No cloud STT bill.
**Tradeoff**: Slightly lower accuracy than Google/Azure STT, but acceptable for interview simulation.

### ADR-008: Pure C# BKT (Not ML.NET)
**Decision**: Implement BKT engine in pure C# (15 lines of Bayes rule).
**Rationale**: The math is simple enough that importing an ML framework is unnecessary. No ML.NET dependency for the core engine.
**Tradeoff**: Can't do deep learning mastery models (overkill anyway for binary mastery state).

### ADR-009: Judge0 (Not Custom Docker Sandbox)
**Decision**: Use Judge0 (open-source code execution API) instead of building a custom Docker sandbox.
**Rationale**: Battle-tested, supports 60+ languages, handles sandboxing/timeout/memory limits/network isolation. Don't reinvent the wheel.
**Tradeoff**: Adds one Docker container (~200MB RAM), but saves weeks of development.

### ADR-010: Monaco Editor (Not Textarea)
**Decision**: Use Monaco Editor (react-monaco-editor) for code input.
**Rationale**: Same engine as VS Code. Syntax highlighting, autocomplete, multi-language support. Same experience as LeetCode.
**Tradeoff**: Adds ~2MB to frontend bundle, but essential for code interview prep.

# VoxMentor — Week 3 Plan

**Version Target:** v1.3.0 · **Team:** 2 members (mixed assignment) · **Duration:** 7 days

> Scope: AI Tutor (RAG coach) + Voice Service + deferred Week-2 frontend
> (#36 progress tracking, #37 dashboard heatmap).
> **No new GitHub issues are created for Week 3 in this rearrangement** —
> existing issues #36/#37 moved into milestone v1.3.0; tutor/RAG/voice work
> will be issued when Week 3 starts.

---

## Goal

Give every student a **conversational coach** grounded in textbook content:

1. Student asks a concept question → RAG retrieves textbook chunks → Ollama answers with citations
2. Answers stream live over SignalR (`/hubs/tutor`) with session polling fallback
3. Voice Service scaffold (FastAPI + faster-whisper STT + Piper TTS) unblocks Week-5 voice mode
4. Dashboard shows mastery heatmap + recent activity (#37); practice shows progress bars (#36)

**End state:** A student can ask "Why is Kadane's O(n)?" → get a streamed,
cited answer → see progress reflected on the dashboard.

---

## Non-Goals (Week 3)

- ~~Mock interview engine~~ → Week 4
- ~~JD Intelligence / readiness~~ → Week 4
- ~~Resume analyzer~~ → Week 5
- ~~Full voice-mode interview~~ → Week 5 (Week 3 ships the service scaffold only)

---

## Prerequisites (from Week 2)

- `POST /api/v1/student/mastery` + KG `/eligible` endpoints live (feed #36/#37 UI)
- Textbook chunk schema (`TextbookChunks` + pgvector column) present
- SignalR hub scaffold present (#10); Redis backplane available (#4 infra)

---

## Workstreams

### A. RAG ingestion pipeline (backend)

- `POST /api/v1/admin/textbook/upload` (multipart `.pdf`/`.txt` + optional `conceptId`) → Hangfire job chunks text, embeds via `nomic-embed-text`, stores pgvector rows (see API.md §8)
- `GET /api/v1/admin/textbook/status/{jobId}` (Pending → Processing → Completed/Failed)
- IVFFlat index on the embedding column for ANN search
- Seed corpus: CTCI excerpts + GFG articles mapped to the 50 concepts

### B. AI Tutor endpoints + streaming (backend)

- `POST /api/v1/tutor/ask` → `202` + `sessionId` (rate limit 10/hour/student → `429`)
- `GET /api/v1/tutor/sessions/{sessionId}` (Pending → Streaming → Completed/Failed)
- `/hubs/tutor`: `AskTutor` → `TutorToken`* → `TutorComplete` / `TutorError`
- Retrieval: top-k chunks by cosine (`<=>`), concept-filtered; prompt builder injects chunks + citations
- Polly circuit breaker around Ollama calls; `503` when Ollama is down

### C. Voice Service scaffold (Python FastAPI, :8001)

- `POST /stt` (audio → text + language + durationMs), `POST /tts` (text → wav), `GET /health`
- faster-whisper + Piper; Dockerized alongside the .NET stack
- YARP `/voice` route → `:8001` (API.md §11)

### D. Deferred Week-2 frontend (#36, #37)

- #36: `MasteryProgressBar` + `ConceptCard` on the practice page (reads Week-2 mastery endpoint)
- #37: `MasteryHeatmap` + `RecentActivity` on the dashboard (reads Week-2 mastery + submission history)

---

## Day Sketch

| Day | Backend | Frontend / Infra |
|---|---|---|
| D1 | RAG ingestion: upload endpoint + chunking job | #36 progress bars |
| D2 | Embeddings + IVFFlat index + seed corpus | #37 dashboard heatmap |
| D3 | `POST /api/v1/tutor/ask` + session store | #37 recent activity |
| D4 | `/hubs/tutor` streaming + circuit breaker | Tutor chat UI (sessions list + stream view) |
| D5 | Voice scaffold: FastAPI STT/TTS + Docker | YARP `/voice` route + rate-limit rules |
| D6 | Prompt/citation quality pass; load-test retrieval | Polish + empty/error states |
| D7 | CI updates; CHANGELOG; tag v1.3.0 | Bug fixes |

---

## End-of-Week Checklist

- [ ] Textbook upload → chunks + embeddings searchable (#A)
- [ ] Tutor ask → streamed cited answer; session poll works (#B)
- [ ] Voice STT/TTS round-trip works via Docker (#C)
- [ ] Dashboard heatmap + progress bars render from Week-2 endpoints (#36, #37)
- [ ] Rate limits enforced (tutor 10/hr → 429)
- [ ] CHANGELOG updated; tag `v1.3.0` on `main`

---

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Ollama latency on long contexts | Streaming feels slow | Top-k=5 cap, `llama3.2:3b`, stream tokens immediately |
| Embedding model download | Slow Docker build | Pre-pull `nomic-embed-text` in Dockerfile |
| Citation hallucination | Wrong chunk refs | Constrain prompt to retrieved chunks; eval on 20 golden Q&As |
| Week-2 spillover | Less tutor capacity | D1–D2 frontend (#36/#37) can proceed in parallel regardless |

---

## GitHub Status (no new issues)

- Milestone **v1.3.0 — Week 3: AI Tutor + RAG + Voice** already exists and now holds #36, #37 (moved from v1.2.0)
- Tutor/RAG/voice issues will be filed when Week 3 kicks off — not now

# VoxMentor — Week 2 Plan (Backend-Endpoint Focus)

**Version Target:** v1.2.0 · **Team:** 2 members (mixed assignment) · **Duration:** 7 days

> **Rearranged:** Week 2 is now backend-endpoint heavy. The practice/learning
> and admin API surface (API.md §4 + §8) gets built this week; dashboard
> heatmap (#36) and progress tracking UI (#37) move to Week 3.

---

## Goal

Ship the **backend API surface for the learning loop** plus the minimum
frontend to exercise it:

1. `POST /api/v1/answer` (live after #45 + #47 merge) → BKT updates mastery
2. `POST /api/v1/student/submit-code` → Judge0 + AI eval + plagiarism + BKT, one call
3. `GET` questions / next-question / mastery / readiness → practice flow is fully API-driven
4. Admin question-bank CRUD + 100 seeded questions
5. Knowledge-graph queries served over HTTP (#49)
6. Plagiarism + Hangfire jobs close out the week

**End state:** A student can register → practice DSA questions via API → get
scored → see mastery improve; every endpoint in API.md §4 + §8 (questions)
is implemented and covered by tests.

---

## Non-Goals (Week 2)

These are explicitly OUT of scope. Don't touch them:

- ~~Dashboard heatmap + recent activity (#37)~~ → Week 3
- ~~Progress tracking UI (#36)~~ → Week 3
- ~~AI Tutor / RAG Coach~~ → Week 3
- ~~Voice integration~~ → Week 5
- ~~Mock interview engine~~ → Week 4
- ~~Resume analyzer~~ → Week 5
- ~~JD Intelligence~~ → Week 4

---

## Already Merged (do not redo)

| Item | Issue | Status |
|---|---|---|
| BKT Engine (pure C#) | #23 | ✅ Merged |
| BktParameters entity + migration | #24 | ✅ Merged |
| Seed BKT params (50 concepts) | #25 | ✅ Merged |
| StudentMastery + LastPracticedAt | #26 | ✅ Merged |
| CodeSubmissions columns | #27 | ✅ Merged |
| SubmitAnswer CQRS pipeline | #28 (PR #47) | ✅ Merged |
| Knowledge-graph seed (50 DSA concepts) | #9 (PR #48) | ✅ Merged |

---

## Prerequisites

```bash
# All must pass:
docker compose ps                    # All containers running
curl http://localhost:5000/health     # Postgres + Redis healthy
curl http://localhost:11434/api/tags  # Ollama models pulled
dotnet build VoxMentor.slnx          # Solution builds
cd web && npm run build              # Frontend builds
```

---

## Day Plan (backend-first)

| Day | Backend endpoints | Frontend / Infra | Issues |
|---|---|---|---|
| **D1** | Merge #45 AnswerController → `POST /api/v1/answer` live | #32 practice page skeleton | #29, #32 |
| **D2** | Merge #50 Judge0 client + `POST /api/v1/execute`; #31 `OllamaCodeEvaluator` | #33 Monaco editor | #30, #31, #33 |
| **D3** | **Submit-code pipeline** `POST /api/v1/student/submit-code` (Judge0 → persist → AI eval → plagiarism hook → BKT → `MasteryUpdated` event) | #34 results panel | #54 (new), #34, #51 |
| **D4** | **Questions** `GET /api/v1/questions`, `GET by id` (hidden cases excluded), adaptive `GET /api/v1/student/next-question`; **Admin bank** CRUD + seed 100 questions | #35 question navigation | #52, #55 (new), #35 |
| **D5** | **Mastery** `GET /api/v1/student/mastery` + `GET readiness`; **#49** KG queries (`prerequisites`, `eligible`) | — | #53 (new), #49 |
| **D6** | **Plagiarism** (embeddings + AST) wired into submit-code | — | #56 (new) |
| **D7** | **Jobs**: BKT-tuning + spaced-repetition decay + tenant query filter; CI updates; tag v1.2.0 | #38 CI/CD | #57 (new), #38, #39 |

Ownership (mixed assignment kept): #52 ansifmk · #53 naheel0 · #54 naheel0 · #55 ansifmk · #56 naheel0 · #57 ansifmk · #49 ansifmk · #51 naheel0.

Dependency order: #47 → #45 → #50 → #31 → #54 → (#52, #55, #53, #49) → #56 → #57 → #38 → #39.

---

## Endpoint → Issue Map (Week 2 backend)

| Endpoint | Issue | Status |
|---|---|---|
| `POST /api/v1/answer` | #29 (PR #45) | In Progress |
| `POST /api/v1/execute` | #30 (PR #50) | In Progress |
| `POST /api/v1/student/submit-code` | #54 (new) | Todo |
| `GET /api/v1/questions`, `GET /{id}` | #52 (new) | Todo |
| `GET /api/v1/student/next-question` | #52 (new) | Todo |
| `GET /api/v1/student/mastery`, `GET readiness` | #53 (new) | Todo |
| `POST/GET /api/v1/admin/questions` + 100-question seed | #55 (new) | Todo |
| `GET /api/concepts/{id}/prerequisites`, `GET /api/students/me/eligible` | #49 | Todo |
| Plagiarism scoring (in-pipeline) | #56 (new) | Todo |
| Hangfire jobs + query filter | #57 (new) | Todo |
| AI code evaluation (in-pipeline) | #31 | Todo |
| Per-submission idempotency | #51 | Todo |

Frontend left in Week 2: #32 practice skeleton, #33 Monaco, #34 results panel, #35 navigation.

---

## Day-by-Day Details

### D1 — Answer endpoint live (backend) + Practice skeleton (frontend)

**Backend:**
1. Get PR #47 re-approved (ansifmk approval was auto-dismissed by later pushes) and merge — `SubmitAnswerHandler` lands on `main`
2. Finish PR #45 `AnswerController` review → merge → verify `POST /api/v1/answer` end-to-end:
   ```json
   // POST /api/v1/answer  {"questionId": "...", "isCorrect": true}
   // → {"previousMastery": 0.1, "newMastery": 0.35, "masteryDelta": 0.25}
   ```

**Frontend (#32):** `web/app/practice/page.tsx` + `QuestionCard` + `CodeEditor` placeholder (unchanged from original spec).

### D2 — Execution + AI eval (backend) + Monaco (frontend)

**Backend:** Merge PR #50 (Judge0 client, `POST /api/v1/execute`, 10s timeout); implement #31 `OllamaCodeEvaluator` (correctness/complexity/style prompt, JSON parse, store in `CodeSubmissions.AiEvaluation`).

**Frontend (#33):** Monaco editor + language selector (unchanged from original spec).

### D3 — Submit-code pipeline (#54) + Results panel (#34)

**#54 pipeline order:** validate → Judge0 execute all test cases → persist `CodeSubmission` → AI eval → plagiarism hook → derive `IsCorrect` → `SubmitAnswerHandler` → publish `MasteryUpdated` (log via null publisher until Redis Streams lands in #4) → single `ApiResponse` envelope (see API.md §4 for shape).

**#51 note:** per-submission idempotency (`MasteryApplied` claim) lands with or immediately after #54 — same files, same PR or stacked PR.

### D4 — Questions + Admin bank (#52, #55) + Navigation (#35)

**#52:** list (paging + concept/difficulty filters), get-by-id (strip hidden cases server-side), adaptive next-question (BKT gap × difficulty target `1 + mastery×9`).
**#55:** Admin CRUD + `scripts/seed-questions.sql` (≥100 questions, all 50 concepts, ≥1 hidden case each) + `scripts/verify-questions.sql`.

### D5 — Mastery + KG queries (#53, #49)

**#53:** mastery profile (`isMastered` at ≥ 0.85) + readiness (`score = 100 × Σ mastery × jdWeight`, gaps, `estimatedWeeksToReady`).
**#49:** `GetPrerequisiteChain` / `GetEligibleConcepts` / `GetAlmostEligibleConcepts` as single-round-trip SQL, exposed under `/api/concepts/{id}/prerequisites` and `/api/students/me/eligible`, with unit tests.

### D6 — Plagiarism (#56)

`nomic-embed-text` embeddings + pgvector cosine (> 0.85) + AST comparison; combined score stored on the submission; threshold tests (copy > 0.9, renamed > 0.7, original < 0.3).

### D7 — Jobs + release (#57, #38, #39)

BKT EM-tuning (2 AM) + decay (3 AM, floor 0.1) + `IUserOwned` global filter; CI updates; CHANGELOG; tag `v1.2.0`.

---

## Database Schema Changes Summary

| Change | Table | Details |
|---|---|---|
| Add column | `StudentMastery` | `LastPracticedAt` (timestamp) — ✅ done (#26) |
| Add columns | `CodeSubmissions` | `PlagiarismScore` (float), `AiEvaluation` (jsonb), `Status` (int) — ✅ done (#27) |
| New table | `BktParameters` | Per-concept BKT params — ✅ done (#24, #25) |
| Concurrency | `StudentMasteries` | `RowVersion → xmin` token — ✅ done (PR #47) |
| (No new schema Week 2 — all endpoint work builds on the above) |

---

## End-of-Week Checklist

### Backend
- [ ] `POST /api/v1/answer` works end-to-end (#29)
- [ ] Code execution via Judge0 works (Python, Java, C++, JS, C#) (#30)
- [ ] `POST /api/v1/student/submit-code` works end-to-end (#54)
- [ ] Questions list/get/next-question live (#52)
- [ ] Mastery + readiness live (#53)
- [ ] Admin bank CRUD + 100 questions seeded (#55)
- [ ] KG eligible/almost-eligible queries live (#49)
- [ ] AI evaluates code quality (#31)
- [ ] Plagiarism flags similar submissions (#56)
- [ ] Per-submission idempotency holds (#51)
- [ ] Hangfire jobs configured + query filter (#57)
- [ ] CI green on every PR (#38)

### Frontend
- [ ] Practice page shows question + Monaco editor (#32, #33)
- [ ] Code submission sends to backend + shows results (#34)
- [ ] Question navigation (prev/next, difficulty filter) works (#35)

### Both
- [ ] All unit tests pass (`dotnet test`)
- [ ] Frontend builds (`npm run build`)
- [ ] `CHANGELOG.md` updated for v1.2.0 (#39)
- [ ] Tag `v1.2.0` created on `main` (#39)
- [ ] Demo: register → practice question via API → see mastery update

---

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Judge0 setup issues | Can't execute code | Use Docker, check ports, test with `print("hello")` first |
| Ollama slow responses | AI eval takes 10s+ | Use `llama3.2:3b`, cache frequent evaluations |
| Scope creep (22 endpoints) | Week overruns | D3 pipeline is the critical path — cut plagiarism to thresholds-only if behind |
| Hidden test-case leak | Students see answers | Strip `hidden` cases server-side in #52; add unit test |
| EF migration conflicts | Database broken | No new schema this week — endpoint-only changes |
| BKT math errors | Wrong mastery scores | Covered by #23 tests; reuse, don't rewrite |

---

## Git Workflow

```bash
# Branch naming
feat/52-questions-endpoints
feat/54-submit-code-pipeline
fix/51-submission-idempotency

# Commit messages
feat(52): add questions list/get/next-question CQRS queries
feat(54): wire submit-code pipeline Judge0-persist-AI-BKT

# PR process
1. Create branch from main
2. Code + commit
3. Push → create PR
4. Get 1 approval from teammate
5. Merge to main
6. Pull latest
```

---

## Reference: BKT Algorithm

```
Parameters per concept:
  P(L₀) = prior knowledge (default: 0.1)
  P(learn) = learning rate (default: 0.3, bounded [0,1])
  P(guess) = P(correct | not learned) (default: 0.2)
  P(slip) = P(incorrect | learned) (default: 0.1)

After observation (correct/incorrect):
  P(C) = P(C|L)×P(L) + P(C|~L)×P(~L)

  If correct:
    P(L|correct) = P(C|L)×P(L) / P(C)
    New mastery = P(L|correct) + (1 - P(L|correct)) × learnRate

  If incorrect:
    P(L|incorrect) = P(C|~L)×P(L) / P(~C)

Mastery thresholds:
  < 0.3  = Not started
  0.3-0.7 = Learning
  0.7-0.85 = Almost mastered
  ≥ 0.85 = Mastered
```

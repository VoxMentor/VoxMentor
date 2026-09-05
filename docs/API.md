# VoxMentor — Complete API Reference

> Every API endpoint needed to complete this project.
>
> Status legend: ✅ Implemented | 🚧 To be built

## Base URLs

| Service | Dev URL | Docker/Prod URL |
|---|---|---|
| Core API (`VoxMentor.Api`) | `http://localhost:5253` | `http://localhost:5000` |
| YARP Gateway (`VoxMentor.Gateway`) | `http://localhost:5106` | `http://localhost:8080` |
| AI Tutor Service | `http://localhost:5050` | `http://localhost:5001` |
| Code Exec Service | `http://localhost:5167` | `http://localhost:5002` |
| Voice Service (Python) | — | `http://localhost:8001` |
| Web (Next.js) | `http://localhost:3000` | — |

The Next.js client proxies `/api/:path*` → `BACKEND_ORIGIN` (`web/next.config.ts`, `web/.env`).

---

## Conventions

### Authentication
- JWT Bearer: `Authorization: Bearer <token>` (Issuer `VoxMentorApi`, Audience `VoxMentorApp`, 120-min expiry).
- The Core API also accepts the JWT from the `access_token` cookie (set on login, `Path=/api/v1`, HttpOnly, SameSite=Lax).
- Refresh token lives in the `refresh_token` cookie (`Path=/api/v1/auth`, HttpOnly).
- For SignalR hubs pass the JWT as `?access_token=<token>` query string.
- Anonymous endpoints: `/api/v1/auth/register`, `/api/v1/auth/login`, `/api/v1/auth/refresh`, `/api/v1/auth/logout`, `/health`.
- Admin endpoints require role `Admin`; everything else requires role `Student` (or any authenticated user).

### Response envelope (all Core API endpoints)
```json
{
  "success": true,
  "message": "Optional message",
  "data": { },
  "errors": null
}
```

### Error format
On failure: `{ "success": false, "message": "...", "errors": { "field": ["reason"] } }`

| HTTP Status | Trigger |
|---|---|
| 400 | Validation failure |
| 401 | Missing/invalid JWT or bad credentials |
| 404 | Resource not found |
| 409 | Conflict (duplicate email, interview already ended) |
| 429 | Rate limited (includes `retryAfter` in message) |
| 500 | Unhandled error |
| 503 | Dependency down (Ollama/Judge0) |

---

# 1. Authentication — ✅ Implemented

Route base: `api/v1/auth` (`src/VoxMentor.Api/Controllers/AuthController.cs`)

### POST /api/v1/auth/register
Register a new user (role `Student`). Anonymous.

Request:
```json
{ "fullName": "Student Name", "email": "student@example.com", "password": "SecurePass123!" }
```

Response `201`:
```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "fullName": "Student Name",
    "email": "student@example.com",
    "role": "Student",
    "createdAt": "2026-08-28T10:00:00Z"
  }
}
```
Errors: `400` validation, `409` email already exists.

### POST /api/v1/auth/login
Login. Anonymous. Sets `access_token` + `refresh_token` HttpOnly cookies.

Request:
```json
{ "email": "student@example.com", "password": "SecurePass123!" }
```

Response `200`:
```json
{ "success": true, "data": { "id": "uuid", "fullName": "Student Name", "email": "student@example.com", "roles": ["Student"] } }
```
Errors: `400` validation, `401` invalid credentials.

### POST /api/v1/auth/refresh
Rotate tokens. No body — reads `refresh_token` cookie, re-sets both cookies.

Response `200`: same shape as login. Error: `401` invalid/expired refresh token.

### POST /api/v1/auth/logout
Invalidates refresh token, deletes both cookies. No body.

Response `200`: `{ "success": true, "data": null }`

### GET /api/v1/auth/me
Current user from JWT claims. Requires auth (header or cookie).

Response `200`:
```json
{ "success": true, "data": { "id": "uuid", "fullName": "Student Name", "email": "student@example.com", "roles": ["Student"] } }
```

---

# 2. Health — ✅ Implemented

### GET /health (Core API)
Checks PostgreSQL. Anonymous.
```json
{ "status": "Healthy", "checks": { "postgres": "Healthy" } }
```
Extend `checks` with `redis`, `ollama`, `judge0` as those are wired up.

### GET /health (Tutor Service, Code Exec Service)
Stubs — `{ "status": "Healthy" }`.

---

# 3. JD Intelligence — 🚧 To be built

### POST /api/v1/jd/upload
Upload a Job Description. LLM (Ollama) parses it into weighted skills; falls back to keyword heuristics when Ollama is offline.

Request:
```json
{ "rawText": "Amazon is hiring an SDE-1. Strong fundamentals in DSA, proficiency in Java/C++/Python..." }
```
Validation: `rawText` required, 50–20000 chars.

Response `200`:
```json
{
  "jdId": "uuid",
  "companyName": "Amazon",
  "role": "SDE-1",
  "technicalSkills": { "DP": 0.35, "Graphs": 0.25, "Arrays": 0.15, "Trees": 0.10, "System Design": 0.15 },
  "hrSkills": { "Leadership": 0.30, "Ownership": 0.25, "Customer Obsession": 0.25, "Conflict Resolution": 0.20 },
  "difficulty": "Medium-Hard",
  "estimatedWeeks": 6,
  "createdAt": "2026-08-28T10:00:00Z"
}
```
Skill weights in each object sum to 1.0.

### GET /api/v1/jd/{jdId}
Parsed JD + Readiness Score + gap analysis + week-by-week roadmap for the current student.

Response `200`:
```json
{
  "jdId": "uuid",
  "companyName": "Amazon",
  "role": "SDE-1",
  "technicalSkills": { "DP": 0.35, "Graphs": 0.25 },
  "hrSkills": { "Leadership": 0.3 },
  "difficulty": "Medium-Hard",
  "readinessScore": 42,
  "gaps": [ { "topic": "DP", "severity": 0.298, "recommendation": "Practice 6 DP problems per week for the next 2 weeks." } ],
  "estimatedWeeks": 6,
  "roadmap": [ { "week": 1, "focus": "DP", "conceptIds": ["uuid-1", "uuid-2"] } ],
  "createdAt": "2026-08-28T10:00:00Z"
}
```
Errors: `404` JD not found / not owned by user.

---

# 4. Practice & Learning — 🚧 Week 2 in progress (issues #52 questions, #53 mastery, #54 submit-code)

### GET /api/v1/student/next-question?jdId={jdId}
Adaptive next question. Selection = BKT mastery gap × JD weight; question difficulty targets `1 + mastery×9` (eased after recent failure). `jdId` optional — without it the weakest concept is chosen.

Response `200`:
```json
{
  "questionId": "uuid",
  "conceptId": "uuid-dp",
  "conceptName": "Dynamic Programming",
  "text": "Given an array of integers, find the maximum sum of a contiguous subarray.",
  "questionType": "Code",
  "difficulty": 6,
  "testCases": [ { "input": "[-2,1,-3,4,-1,2,1,-5,4]", "expected": "6", "hidden": false } ],
  "isomorphicInstanceId": "uuid-unique-per-student"
}
```
Hidden test cases are excluded from the payload. Errors: `404` no JD / no questions in bank.

### POST /api/v1/student/submit-code
Submit code → sandboxed execution (Judge0) + AI evaluation + plagiarism check + BKT mastery update.

Request:
```json
{ "questionId": "uuid", "code": "def max_subarray(nums): ...", "language": "python" }
```
Validation: `language` ∈ `python | java | cpp | c | javascript | csharp`; `code` ≤ 50000 chars.

Response `200`:
```json
{
  "submissionId": "uuid",
  "testCasesPassed": 8,
  "testCasesTotal": 10,
  "executionTimeMs": 45,
  "memoryUsageKb": 12300,
  "aiEvaluation": {
    "correctness": { "score": 8, "maxScore": 10, "feedback": "2 hidden test cases failed on edge cases." },
    "timeComplexity": { "score": "O(n)", "isOptimal": true, "feedback": "Optimal. Good use of Kadane's." },
    "spaceComplexity": { "score": "O(1)", "isOptimal": true },
    "codeStyle": { "score": 7, "maxScore": 10, "feedback": "Clear names. Add a docstring." }
  },
  "bktUpdate": { "previousMastery": 0.45, "newMastery": 0.52, "masteryDelta": 0.07 },
  "plagiarismScore": 0.12,
  "submittedAt": "2026-08-28T10:05:00Z"
}
```
BKT params: p(learn)=0.3, p(guess)=0.2, p(slip)=0.1; correct ⇔ all test cases pass. Mastery ≥ 0.85 = mastered.
Side effect: pushes `MasteryUpdated` on `/hubs/mastery`.
Errors: `404` question not found.

### GET /api/v1/student/mastery
Mastery profile across all concepts + overall readiness (against latest JD, or average mastery if no JD).

Response `200`:
```json
{
  "concepts": [
    { "conceptId": "uuid", "name": "Arrays", "mastery": 0.85, "isMastered": true, "correctAttempts": 9, "incorrectAttempts": 2 },
    { "conceptId": "uuid", "name": "DP", "mastery": 0.52, "isMastered": false, "correctAttempts": 3, "incorrectAttempts": 4 }
  ],
  "overallReadiness": 42
}
```

### GET /api/v1/student/readiness?jdId={jdId}
Readiness Score breakdown for a JD (defaults to the user's latest JD).

Response `200`:
```json
{
  "score": 42,
  "maxScore": 100,
  "breakdown": [ { "topic": "DP", "mastery": 0.15, "jdWeight": 0.35, "contribution": 5.25 } ],
  "gaps": [ { "topic": "DP", "severity": 0.298, "recommendation": "Practice 6 DP problems per week for the next 2 weeks." } ],
  "estimatedWeeksToReady": 6
}
```
Formula: `score = 100 × Σ mastery(topic) × jdWeight(topic)`; `severity = jdWeight × (1 − mastery)`.
Errors: `404` no JD found.

---

# 5. AI Coach (RAG Tutor) — 🚧 To be built

### POST /api/v1/tutor/ask
Ask the AI Coach. Answer is generated asynchronously — stream it on `/hubs/tutor` or poll the session endpoint.
**Rate limit:** 10 requests/hour per student (free tier) → `429`.

Request:
```json
{ "conceptId": "uuid-dp", "question": "Why is Kadane's algorithm O(n)?" }
```
Validation: `question` required, ≤ 2000 chars; `conceptId` optional (must exist).

Response `202`:
```json
{ "sessionId": "uuid", "message": "Response is being generated. Stream via /hubs/tutor or poll GET /api/v1/tutor/sessions/{sessionId}." }
```

### GET /api/v1/tutor/sessions/{sessionId}
Poll a tutor session's status/answer.

Response `200`:
```json
{
  "sessionId": "uuid",
  "conceptId": "uuid-dp",
  "question": "Why is Kadane's algorithm O(n)?",
  "answer": "Kadane's algorithm makes a single pass...",
  "status": "Completed",
  "totalTokens": 145,
  "createdAt": "2026-08-28T11:00:00Z",
  "completedAt": "2026-08-28T11:00:06Z"
}
```
`status` ∈ `Pending | Streaming | Completed | Failed`. Errors: `404` session not found / not owned.

---

# 6. Mock Interviews — 🚧 To be built

### POST /api/v1/mock/start
Start an AI mock interview. Opening message is generated from the JD's company/role and an adaptively chosen problem.
**Rate limit:** 1/day free tier, 5/day Pro → `429`.

Request:
```json
{ "jdId": "uuid", "type": "Technical", "voiceMode": true }
```
`type` ∈ `Technical | Hr | Both`. `jdId` optional.

Response `202`:
```json
{
  "interviewId": "uuid",
  "type": "Technical",
  "status": "InProgress",
  "duration": 45,
  "startedAt": "2026-08-28T14:00:00Z",
  "openingMessage": "Hi, I'm your interviewer at Amazon for the SDE-1 position. Let's start with your first problem: ..."
}
```
Errors: `404` JD not found, `429` daily limit.

### POST /api/v1/mock/{interviewId}/answer
Submit the candidate's answer; the interviewer replies with a follow-up or the next problem (up to 3 problems).

Request:
```json
{ "message": "I'd use Kadane's algorithm — track the running sum..." }
```

Response `200`:
```json
{ "interviewerMessage": "Good. What's the space complexity? Can you handle all-negative arrays?", "turnNumber": 4, "interviewOngoing": true }
```
Errors: `404` interview not found, `409` interview already ended, `400` empty message.

### POST /api/v1/mock/{interviewId}/complete
End the interview and get the AI review (score, per-problem feedback, next steps).

Response `200`:
```json
{
  "score": 68,
  "type": "Technical",
  "technicalReview": {
    "problem1": { "solved": true, "complexity": "Discussed", "feedback": "Candidate articulated a complete approach." },
    "problem2": { "solved": false, "complexity": "Not completed", "feedback": "Answer lacked detail — practice explaining approach and complexity clearly." }
  },
  "readinessDelta": { "before": 62, "after": 62, "delta": 0 },
  "nextSteps": "Good foundation. Focus on explaining complexity and edge cases out loud."
}
```
Errors: `404` not found, `409` already ended.

### GET /api/v1/mock/{interviewId}/review
Fetch the stored review of a completed interview.

Response `200`:
```json
{
  "interviewId": "uuid",
  "status": "Completed",
  "score": 68,
  "review": { "score": 68, "type": "Technical", "technicalReview": { }, "readinessDelta": { }, "nextSteps": "..." },
  "startedAt": "2026-08-28T14:00:00Z",
  "completedAt": "2026-08-28T14:40:00Z"
}
```

### GET /api/v1/mock/history
List the user's interviews, newest first.

Response `200`:
```json
{
  "interviews": [
    { "interviewId": "uuid-1", "type": "Technical", "status": "Completed", "score": 55, "date": "2026-08-21T14:00:00Z" },
    { "interviewId": "uuid-2", "type": "Hr", "status": "InProgress", "score": null, "date": "2026-08-28T14:00:00Z" }
  ]
}
```

---

# 7. Resume ATS — 🚧 To be built

### POST /api/v1/resume/analyze
Upload resume + optional JD for ATS analysis. `multipart/form-data`: `file` (`.pdf` or `.txt`) + optional `jdId`. Without `jdId`, keywords are extracted from the raw JD text heuristically.

Response `200`:
```json
{
  "analysisId": "uuid",
  "matchScore": 72,
  "keywordMatch": {
    "matched": ["Python", "Databases", "Teamwork"],
    "missing": ["System Design", "DP", "Leadership"]
  },
  "atsParseability": {
    "score": 85,
    "issues": ["No 'projects' section detected — ATS parsers expect clearly labeled sections."]
  },
  "suggestions": [
    "Add 'System Design' to your resume — it appears in the JD but is missing from your resume.",
    "Use plain, clearly-labeled section headings (Education, Experience, Skills, Projects) instead of tables or columns."
  ]
}
```
Errors: `400` missing/unsupported file, `404` JD not found.

---

# 8. Admin — 🚧 To be built (requires role `Admin`); question-bank endpoints are Week 2 in progress (issue #55)

### POST /api/v1/admin/concepts
Create a DSA concept.

Request:
```json
{ "name": "Backtracking", "category": "DSA", "difficulty": 7 }
```
Response `201`: `{ "conceptId": "uuid", "name": "Backtracking", "category": "DSA", "difficulty": 7 }`
Errors: `400` validation (difficulty 1–10), `409` duplicate concept name.

### POST /api/v1/admin/prerequisites
Add a prerequisite edge to the knowledge graph.

Request:
```json
{ "conceptId": "uuid-backtracking", "prerequisiteId": "uuid-recursion" }
```
Response `201`: `{ "conceptId": "uuid-backtracking", "prerequisiteId": "uuid-recursion" }`
Errors: `404` either concept missing, `409` edge exists, `400` self-reference.

### POST /api/v1/admin/questions
Add a question (with test cases + rubric) to the bank.

Request:
```json
{
  "conceptId": "uuid-dp",
  "text": "Given a grid, find the minimum path sum from top-left to bottom-right.",
  "questionType": "Code",
  "difficulty": 6,
  "testCases": [
    { "input": "[[1,3,1],[1,5,1],[4,2,1]]", "expected": "7", "hidden": false, "isExample": true },
    { "input": "[[1,2],[5,6],[1,1]]", "expected": "9", "hidden": true, "isExample": false }
  ],
  "templateVariables": null,
  "rubric": [
    { "criterion": "Correct DP recurrence", "points": 3 },
    { "criterion": "Handles edge cases", "points": 2 }
  ]
}
```
Response `201`: `{ "questionId": "uuid", "conceptId": "uuid-dp", "testCasesCreated": 2 }`
Errors: `404` concept missing, `400` no test cases / invalid difficulty.

### POST /api/v1/admin/textbook/upload
Upload prep content (CTCI, GFG notes — `.pdf`/`.txt`) for chunking + concept mapping. `multipart/form-data`: `file` + optional `conceptId`.

Response `202`:
```json
{ "jobId": "uuid", "message": "Background job started. Check GET /api/v1/admin/textbook/status/{jobId}." }
```

### GET /api/v1/admin/textbook/status/{jobId}
Poll ingestion job progress.

Response `200`:
```json
{
  "jobId": "uuid",
  "fileName": "cracking-the-coding-interview.pdf",
  "status": "Processing",
  "totalChunks": 240,
  "processedChunks": 118,
  "error": null,
  "createdAt": "2026-08-28T09:00:00Z",
  "completedAt": null
}
```
`status` ∈ `Pending | Processing | Completed | Failed`. Errors: `404` job not found.

---

# 9. Voice Service (Python FastAPI, port 8001) — 🚧 To be built

| Method + Route | Body | Response |
|---|---|---|
| `POST /stt` | `multipart/form-data` audio (wav/webm) | `{ "text": "...", "language": "en", "durationMs": 3200 }` |
| `POST /tts` | `{ "text": "...", "voice": "en_IN-female" }` | `audio/wav` stream |
| `GET /health` | — | `{ "status": "Healthy" }` |

Stack: faster-whisper (STT) + Piper (TTS). Used by the interview flow when `voiceMode=true`.

---

# 10. SignalR Hubs — 🚧 To be built

Auth: JWT via `?access_token=<token>` query string on the hub URL.

### /hubs/tutor — AI Coach streaming
| Event | Direction | Data |
|---|---|---|
| `AskTutor` | Client → Server | `{ conceptId, question }` |
| `TutorToken` | Server → Client | `"token text"` (streamed) |
| `TutorComplete` | Server → Client | `{ sessionId, totalTokens }` |
| `TutorError` | Server → Client | `{ sessionId, message }` |

### /hubs/mastery — Real-time mastery updates
| Event | Direction | Data |
|---|---|---|
| `MasteryUpdated` | Server → Client | `{ conceptId, conceptName, newMastery, delta }` |
| `ReadinessChanged` | Server → Client | `{ newScore, delta }` |

### /hubs/interview — Mock interview real-time (voice + text)
| Event | Direction | Data |
|---|---|---|
| `StartMock` | Client → Server | `{ jdId, type, voiceMode }` |
| `SendAnswer` | Client → Server | `{ interviewId, message }` |
| `CompleteMock` | Client → Server | `{ interviewId }` |
| `InterviewerMessage` | Server → Client | `"text"` (streamed) |
| `InterviewerVoice` | Server → Client | audio chunk (via Voice Service) |
| `InterviewFollowUp` | Server → Client | `"follow-up question"` |
| `InterviewComplete` | Server → Client | `{ interviewId, score }` |

---

# 11. Gateway Routing (YARP, port 8080) — 🚧 To be built

| Path prefix | Target |
|---|---|
| `/api/v1/auth`, `/api/v1/jd`, `/api/v1/student`, `/api/v1/mock`, `/api/v1/resume`, `/api/v1/admin` | Core API (`:5000`) |
| `/api/v1/tutor` | Core API (`:5000`) — move to Tutor Service (`:5001`) when extracted |
| `/voice` | Voice Service (`:8001`) |
| `/hubs/*` | Core API (`:5000`) — WebSocket upgrade |
| `/health` | Core API (`:5000`) |

Gateway responsibilities: JWT validation, rate limiting, CORS, request routing, health checks.

---

# 12. External Dependencies (consumed, not exposed)

| Dependency | URL | Used for |
|---|---|---|
| Ollama | `http://localhost:11434` | `llama3.2:3b` chat (tutor, JD parse, code eval, interview), `nomic-embed-text` embeddings (768-dim, RAG retrieval) |
| Judge0 | `http://localhost:2358` | Sandboxed code execution (language ids: python=71, java=62, cpp=54, c=50, javascript=63, csharp=51) |
| PostgreSQL 15 + pgvector | `localhost:5432` | Relational + knowledge graph (recursive CTE) + vector storage (textbook chunks) |
| Redis 7 | `localhost:6379` | Cache, SignalR backplane, Redis Streams event bus |

### Internal domain events (Redis Streams)
`MasteryUpdated`, `CodeSubmitted`, `MockInterviewCompleted`, `PlagiarismDetected`

---

# 13. Data Model (tables to create)

| Table | Key columns |
|---|---|
| `Concepts` | Id, Name, Category, Difficulty (1–10) |
| `ConceptPrerequisites` | ConceptId → PrerequisiteId (graph edges) |
| `Questions` | Id, ConceptId, Text, QuestionType, Difficulty, TemplateVariablesJson, RubricJson |
| `QuestionTestCases` | Id, QuestionId, Input, Expected, Hidden, IsExample |
| `StudentMasteries` | Id, UserId, ConceptId, Mastery (0–1), CorrectAttempts, IncorrectAttempts — unique (UserId, ConceptId) |
| `JobDescriptions` | Id, UserId, RawText, CompanyName, Role, TechnicalSkillsJson, HrSkillsJson, Difficulty, EstimatedWeeks |
| `CodeSubmissions` | Id, UserId, QuestionId, Code, Language, TestCasesPassed/Total, ExecutionTimeMs, MemoryUsageKb, AiEvaluationJson, PlagiarismScore, MasteryBefore/After |
| `MockInterviews` | Id, UserId, JdId?, Type, Status, Score?, TranscriptJson, ReviewJson, StartedAt, CompletedAt? |
| `ResumeAnalyses` | Id, UserId, JdId?, FileName, MatchScore, KeywordMatchJson, AtsParseabilityJson, SuggestionsJson |
| `TutorSessions` | Id, UserId, ConceptId?, Question, Answer, Status, TotalTokens |
| `TextbookJobs` | Id, FileName, Status, TotalChunks, ProcessedChunks, Error? |
| `TextbookChunks` | Id, JobId, ConceptId?, Content, Source (+ pgvector embedding column) |

---

# Endpoint Count Summary

| Area | Endpoints | Status |
|---|---|---|
| Auth | 5 (register, login, refresh, logout, me) | ✅ Done |
| Health | 3 (Core, Tutor, CodeExec) | ✅ Done |
| JD Intelligence | 2 (upload, get) | 🚧 |
| Practice/Learning | 6 (questions list/get, next-question, submit-code, mastery, readiness) | 🚧 Week 2 (#52–#54) |
| AI Coach | 2 REST (ask, session) + hub | 🚧 |
| Mock Interviews | 5 REST (start, answer, complete, review, history) + hub | 🚧 |
| Resume ATS | 1 (analyze) | 🚧 |
| Admin | 5 (concepts, prerequisites, questions, textbook upload, textbook status) | 🚧 (questions: Week 2 #55) |
| Voice Service | 3 (stt, tts, health) | 🚧 |
| Gateway | YARP routing + rate limiting | 🚧 |
| **Total** | **31 REST + 3 hubs** | 8 done / 23 to build |

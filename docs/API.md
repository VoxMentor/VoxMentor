# VoxMentor — API Documentation

Base URL: `http://localhost:8080` (via YARP Gateway) or `http://localhost:5000` (direct to Core API)

Authentication: All endpoints (except `/auth/*` and `/health`) require `Authorization: Bearer <JWT>` header.

Error format: RFC 7807 Problem Details (JSON with `type`, `title`, `status`, `detail`, `requestId`).

---

## Authentication

### POST /api/v1/auth/register
Register a new user.

**Request:**
```json
{
  "email": "student@example.com",
  "password": "SecurePass123!",
  "fullName": "Student Name",
  "preferredLanguage": "en"
}
```

**Response (201 Created):**
```json
{
  "userId": "uuid-here",
  "email": "student@example.com",
  "token": "eyJhbGciOi...",
  "refreshToken": "refresh-token-here"
}
```

### POST /api/v1/auth/login
Login and receive JWT + refresh token.

**Request:**
```json
{
  "email": "student@example.com",
  "password": "SecurePass123!"
}
```

**Response (200 OK):**
```json
{
  "userId": "uuid-here",
  "token": "eyJhbGciOi...",
  "refreshToken": "refresh-token-here",
  "expiresAt": "2026-08-18T15:30:00Z"
}
```

### POST /api/v1/auth/refresh
Rotate refresh token (old token invalidated).

**Request:**
```json
{
  "refreshToken": "old-refresh-token"
}
```

**Response (200 OK):**
```json
{
  "token": "new-jwt-token",
  "refreshToken": "new-refresh-token"
}
```

---

## JD Intelligence

### POST /api/v1/jd/upload
Upload a Job Description. AI parses it and extracts weighted skills + HR requirements.

**Request:**
```json
{
  "rawText": "Amazon is hiring an SDE-1. Strong fundamentals in DSA, proficiency in Java/C++/Python, understanding of distributed systems. Candidates should demonstrate ownership and customer obsession..."
}
```

**Response (200 OK):**
```json
{
  "jdId": "uuid-here",
  "companyName": "Amazon",
  "role": "SDE-1",
  "technicalSkills": {
    "DP": 0.35,
    "Graphs": 0.25,
    "Arrays": 0.15,
    "Trees": 0.10,
    "SystemDesign": 0.15
  },
  "hrSkills": {
    "Leadership": 0.30,
    "Ownership": 0.25,
    "CustomerObsession": 0.25,
    "ConflictResolution": 0.20
  },
  "difficulty": "Medium-Hard",
  "estimatedWeeks": 6,
  "createdAt": "2026-08-18T10:00:00Z"
}
```

### GET /api/v1/jd/{jdId}
Get parsed JD + Readiness Score + roadmap.

**Response (200 OK):**
```json
{
  "jdId": "uuid-here",
  "companyName": "Amazon",
  "readinessScore": 42,
  "gaps": [
    {
      "topic": "DP",
      "mastery": 0.15,
      "jdWeight": 0.35,
      "severity": 0.298
    }
  ],
  "estimatedWeeks": 6,
  "roadmap": [
    { "week": 1, "focus": "DP", "conceptIds": ["uuid-1", "uuid-2"] },
    { "week": 2, "focus": "Graphs", "conceptIds": ["uuid-3"] }
  ]
}
```

---

## Practice & Learning

### GET /api/v1/student/next-question?jdId={jdId}
Get the next adaptive question based on mastery + JD weights.

**Response (200 OK):**
```json
{
  "questionId": "uuid-here",
  "conceptId": "uuid-dp",
  "conceptName": "Dynamic Programming",
  "text": "Given an array of integers, find the maximum sum of a contiguous subarray.",
  "questionType": "Code",
  "difficulty": 6,
  "testCases": [
    {
      "input": "[-2, 1, -3, 4, -1, 2, 1, -5, 4]",
      "expected": "6",
      "hidden": false
    }
  ],
  "isomorphicInstanceId": "uuid-unique-per-student"
}
```

### POST /api/v1/student/submit-code
Submit code for execution + AI evaluation.

**Request:**
```json
{
  "questionId": "uuid-here",
  "code": "def max_subarray(nums):\n    max_sum = nums[0]\n    current = nums[0]\n    for n in nums[1:]:\n        current = max(n, current + n)\n        max_sum = max(max_sum, current)\n    return max_sum",
  "language": "python"
}
```

**Response (200 OK):**
```json
{
  "submissionId": "uuid-here",
  "testCasesPassed": 8,
  "testCasesTotal": 10,
  "executionTimeMs": 45,
  "memoryUsageKb": 12300,
  "aiEvaluation": {
    "correctness": { "score": 8, "maxScore": 10, "feedback": "2 hidden test cases failed on edge cases (empty array, single element)." },
    "timeComplexity": { "score": "O(n)", "isOptimal": true, "feedback": "O(n) is optimal. Good use of Kadane's algorithm." },
    "spaceComplexity": { "score": "O(1)", "isOptimal": true },
    "codeStyle": { "score": 7, "maxScore": 10, "feedback": "Variable names are clear. Add a docstring." }
  },
  "bktUpdate": {
    "previousMastery": 0.45,
    "newMastery": 0.52,
    "masteryDelta": 0.07
  },
  "plagiarismScore": 0.12,
  "submittedAt": "2026-08-18T10:05:00Z"
}
```

### GET /api/v1/student/mastery
Get mastery profile for all concepts.

**Response (200 OK):**
```json
{
  "concepts": [
    { "conceptId": "uuid", "name": "Arrays", "mastery": 0.85, "isMastered": true },
    { "conceptId": "uuid", "name": "DP", "mastery": 0.52, "isMastered": false },
    { "conceptId": "uuid", "name": "Graphs", "mastery": 0.30, "isMastered": false }
  ],
  "overallReadiness": 42
}
```

### GET /api/v1/student/readiness?jdId={jdId}
Get current Readiness Score for a specific JD.

**Response (200 OK):**
```json
{
  "readinessScore": 42,
  "maxScore": 100,
  "breakdown": [
    { "topic": "DP", "mastery": 0.15, "jdWeight": 0.35, "contribution": 5.25 },
    { "topic": "Graphs", "mastery": 0.30, "jdWeight": 0.25, "contribution": 7.5 }
  ],
  "gaps": [
    { "topic": "DP", "severity": 0.298, "recommendation": "Practice 2 DP problems/day for 2 weeks" }
  ],
  "estimatedWeeksToReady": 6
}
```

---

## AI Coach

### POST /api/v1/tutor/ask
Ask the RAG AI Coach a question. Response streams via SignalR.

**Rate limit:** 10 requests per hour per student (free tier).

**Request:**
```json
{
  "conceptId": "uuid-dp",
  "question": "Why is Kadane's algorithm O(n) and not O(n^2)?"
}
```

**Response (202 Accepted):**
```json
{
  "sessionId": "uuid-here",
  "message": "Streaming response via SignalR. Listen on 'TutorToken' event."
}
```

**SignalR events (client receives):**
```
event: TutorToken
data: "Kadane's"

event: TutorToken
data: " algorithm"

event: TutorToken
data: " is O(n) because..."

event: TutorComplete
data: { "sessionId": "uuid-here", "totalTokens": 145 }
```

---

## Mock Interviews

### POST /api/v1/mock/start
Start an AI mock interview.

**Rate limit:** 1 per day (free tier), 5 per day (Pro tier).

**Request:**
```json
{
  "jdId": "uuid-here",
  "type": "Technical",
  "voiceMode": true
}
```

**Response (202 Accepted):**
```json
{
  "interviewId": "uuid-here",
  "type": "Technical",
  "status": "InProgress",
  "duration": 45,
  "startedAt": "2026-08-18T14:00:00Z"
}
```

**SignalR events (client receives during interview):**
```
event: InterviewerMessage
data: "Hi, I'm your Amazon interviewer today. I'll give you 2 coding problems and 1 system design question."

event: InterviewerMessage
data: "Let's start. Here's your first problem: Given an array, find the maximum sum of a contiguous subarray."

event: InterviewFollowUp
data: "Your solution is O(n^2). Can you optimize to O(n)?"

event: InterviewComplete
data: { "interviewId": "uuid", "score": 68 }
```

### GET /api/v1/mock/{interviewId}/review
Get post-interview AI review.

**Response (200 OK):**
```json
{
  "interviewId": "uuid-here",
  "score": 68,
  "type": "Technical",
  "technicalReview": {
    "problem1": { "solved": true, "complexity": "O(n)", "feedback": "Good Kadane's implementation." },
    "problem2": { "solved": false, "complexity": "O(n^2)", "feedback": "DP recurrence was incorrect. Practice these 5 problems." },
    "systemDesign": { "score": 6, "maxScore": 10, "feedback": "Good approach but missed caching layer." }
  },
  "readinessDelta": { "before": 62, "after": 68, "delta": 6 },
  "nextSteps": "Practice 3 more DP sessions to reach 85% readiness."
}
```

### GET /api/v1/mock/history
List past mock interviews.

**Response (200 OK):**
```json
{
  "interviews": [
    {
      "interviewId": "uuid-1",
      "type": "Technical",
      "score": 55,
      "date": "2026-08-11T14:00:00Z"
    },
    {
      "interviewId": "uuid-2",
      "type": "HR",
      "score": 70,
      "date": "2026-08-13T14:00:00Z"
    }
  ]
}
```

---

## Resume

### POST /api/v1/resume/analyze
Upload resume + JD for ATS analysis.

**Request:** `multipart/form-data`
```
file: resume.pdf
jdId: uuid-here
```

**Response (200 OK):**
```json
{
  "matchScore": 72,
  "keywordMatch": {
    "matched": ["Python", "PostgreSQL", "DSA"],
    "missing": ["AWS", "Distributed Systems", "Docker"]
  },
  "atsParseability": {
    "score": 85,
    "issues": ["Table in education section — ATS may not parse correctly"]
  },
  "suggestions": [
    "Add 'AWS' to skills section — it's in the JD but not your resume.",
    "Remove the table in education section — use bullet points instead.",
    "Add 'Distributed Systems' keyword — mentioned 3 times in JD."
  ]
}
```

---

## Admin

### POST /api/v1/admin/concepts
Create a new DSA concept.

**Request:**
```json
{
  "name": "Backtracking",
  "category": "DSA",
  "difficulty": 7
}
```

### POST /api/v1/admin/prerequisites
Add a prerequisite edge.

**Request:**
```json
{
  "conceptId": "uuid-backtracking",
  "prerequisiteId": "uuid-recursion"
}
```

### POST /api/v1/admin/questions
Add a question to the bank.

**Request:**
```json
{
  "conceptId": "uuid-dp",
  "text": "Given a grid, find the minimum path sum from top-left to bottom-right.",
  "questionType": "Code",
  "difficulty": 6,
  "testCases": [
    { "input": "[[1,3,1],[1,5,1],[4,2,1]]", "expected": "7", "hidden": false }
  ],
  "templateVariables": null,
  "rubric": [
    { "criterion": "Correct DP recurrence", "points": 3 },
    { "criterion": "Handles edge cases", "points": 2 }
  ]
}
```

### POST /api/v1/admin/textbook/upload
Upload interview prep content (CTCI, GFG) for auto-embedding.

**Request:** `multipart/form-data`
```
file: cracking-the-coding-interview.pdf
conceptId: uuid-dp (optional — auto-detected from section titles)
```

**Response (202 Accepted):**
```json
{
  "jobId": "uuid-here",
  "message": "Background job started. Chunks will be embedded via Ollama nomic-embed-text. Check progress via GET /api/v1/admin/textbook/status/{jobId}."
}
```

---

## Health

### GET /health
Check health of all services. No auth required.

**Response (200 OK):**
```json
{
  "status": "Healthy",
  "checks": {
    "postgres": "Healthy",
    "redis": "Healthy",
    "ollama": "Healthy",
    "judge0": "Healthy"
  },
  "uptime": "01:23:45"
}
```

---

## SignalR Hubs

### /hubs/tutor — AI Coach streaming
| Event | Direction | Data |
|---|---|---|
| `AskTutor` | Client → Server | `{ conceptId, question }` |
| `TutorToken` | Server → Client | `"token text"` (streamed) |
| `TutorComplete` | Server → Client | `{ sessionId, totalTokens }` |

### /hubs/mastery — Real-time mastery updates
| Event | Direction | Data |
|---|---|---|
| `MasteryUpdated` | Server → Client | `{ conceptId, newMastery, delta }` |
| `ReadinessChanged` | Server → Client | `{ newScore, delta }` |

### /hubs/interview — Mock interview real-time
| Event | Direction | Data |
|---|---|---|
| `StartMock` | Client → Server | `{ jdId, type, voiceMode }` |
| `InterviewerMessage` | Server → Client | `"text"` (streamed) |
| `InterviewerVoice` | Server → Client | `audioChunk` (WebRTC) |
| `InterviewFollowUp` | Server → Client | `"follow-up question"` |
| `InterviewComplete` | Server → Client | `{ interviewId, score }` |

---

## Error Format (RFC 7807)

All error responses follow this format:

```json
{
  "type": "https://voxmentor.ai/errors/subscription-expired",
  "title": "Subscription expired",
  "status": 402,
  "detail": "Your free tier daily limit for AI Coach has been reached (10/10). Upgrade to Pro for unlimited access.",
  "instance": "/api/v1/tutor/ask",
  "requestId": "req-9a8b7c6d",
  "retryAfter": 3600
}
```

| HTTP Status | Meaning |
|---|---|
| 400 | Bad Request (validation error) |
| 401 | Unauthorized (missing/invalid JWT) |
| 403 | Forbidden (wrong role) |
| 404 | Not Found |
| 409 | Conflict (duplicate submission) |
| 429 | Too Many Requests (rate limited) |
| 500 | Internal Server Error |
| 503 | Service Unavailable (Ollama/Judge0 down) |

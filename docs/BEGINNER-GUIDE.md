# VoxMentor — Beginner's Guide

> New here? Start with this page. It explains what VoxMentor is, what it does, and where to find everything else.

---

## 1. What Is VoxMentor?

VoxMentor is an AI-powered platform that helps college students prepare for job interviews.

1. You **paste a job description** (e.g., "Amazon SDE-1").
2. The AI **analyzes it** and tells you exactly what skills the company wants.
3. You **practice** problems at your level.
4. You take **mock interviews** (technical + HR, voice or text).
5. You get a **Readiness Score** — when you hit 85%+, you're ready to apply.

**In one sentence:** Paste a JD → practice → mock-interview → get ready → get the job.

---

## 2. The 11 Core Features

| # | Feature | What It Does (Plain English) |
|---|---|---|
| 1 | JD Intelligence Engine | Reads a job description and lists the skills it needs (e.g., DP 35%, Graphs 25%). |
| 2 | Readiness Score | A 0–100 number showing how ready you are for a specific job. Ready = 85%+. |
| 3 | BKT Mastery Tracking | Tracks whether you truly mastered a topic (the same algorithm Khan Academy uses). |
| 4 | Adaptive Problem Selection | Picks the next problem at exactly your level. |
| 5 | Code Execution + AI Evaluation | Runs your code against test cases, then evaluates complexity and style. |
| 6 | Isomorphic Question Generation | Every student gets a different problem at the same difficulty — no copying. |
| 7 | RAG AI Coach | A 24/7 tutor that answers from real interview prep books, never makes things up. |
| 8 | AI Technical Mock Interview | A realistic 45-minute technical interview with voice. |
| 9 | AI HR Mock Interview | A behavioral interview that scores your STAR-method answers. |
| 10 | Spaced Repetition | Reminds you to review old topics before you forget them. |
| 11 | Resume ATS Analyzer | Checks your resume against the job description so it passes the ATS bot. |

---

## 3. How It Works Under the Hood

VoxMentor has 4 main parts:

- **Main App (.NET 8)** — the brain. Login, scoring, choosing problems. Fast (50–200ms).
- **AI Tutor Service** — the 24/7 tutor. Streams answers from interview prep books (RAG).
- **Code Execution Service** — runs your code in a safe sandbox (Judge0) + AI evaluation.
- **Voice Service (Python)** — speech-to-text (Whisper) + text-to-speech (Piper).

They are separated so that if one part crashes (e.g., the AI tutor), the rest keeps working.

---

## 4. Tech Stack (One Sentence Each)

| Tool | What It Does |
|---|---|
| .NET 8 | Backend framework (the brain). |
| Next.js 14 | Frontend (what you see in the browser). |
| PostgreSQL + pgvector | Database + AI search by meaning. |
| Ollama + Llama 3.2 | Free local AI (no cloud API costs). |
| Judge0 | Runs your code in a sandbox. |
| Redis | Cache + real-time messaging. |
| Docker | Runs the whole app with one command: `docker compose up`. |

---

## 5. Where to Go Next

| Doc | Read it when... |
|---|---|
| `docs/PROJECT-STRUCTURE.md` | You want to know where every file lives. |
| `docs/SETUP.md` | You want to run the project locally. |
| `docs/ARCHITECTURE.md` | You want the deep technical design + decisions (ADRs). |
| `docs/API.md` | You want to call the backend endpoints. |
| `docs/CHANGELOG.md` | You want to see what changed each week. |
| `docs/WEEK1-PLAN.md` | You want the team's week-1 task plan. |

---

*VoxMentor — Your voice. Your mentor. Your offer.*
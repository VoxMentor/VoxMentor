# VoxMentor — Week 2 Plan

**Version Target:** v1.2.0 · **Team:** 2 members · **Duration:** 7 days

---

## Goal

Add the **learning engine core** — the brain of VoxMentor:

1. Student answers a question → BKT updates mastery
2. Code runs in a sandbox (Judge0) → test cases pass/fail
3. AI evaluates code quality (correctness, complexity, style)
4. Plagiarism detection flags similar submissions
5. Frontend shows practice flow + mastery progress

**End state:** A student can register → upload a JD → practice DSA questions → get scored → see mastery improve.

---

## Non-Goals (Week 2)

These are explicitly OUT of scope. Don't touch them:

- ~~AI Tutor / RAG Coach~~ → Week 3
- ~~Voice integration~~ → Week 5
- ~~Mock interview engine~~ → Week 4
- ~~Resume analyzer~~ → Week 5
- ~~JD Intelligence~~ → Week 4

---

## Prerequisites (Must Be Done First)

Before starting Day 1, verify Week 1 is complete:

```bash
# All must pass:
docker compose ps                    # All containers running
curl http://localhost:5000/health     # Postgres + Redis healthy
curl http://localhost:11434/api/tags  # Ollama models pulled
dotnet build VoxMentor.slnx          # Solution builds
cd web && npm run build              # Frontend builds
```

If anything fails, fix it before starting Week 2.

---

## Role Split

| | Member A — Backend (.NET) | Member B — Frontend + Infra |
|---|---|---|
| **D1** | BKT Engine (pure C# math, ~50 lines) | Practice page skeleton (question display + code editor) |
| **D2** | StudentMastery entity + EF migration + seed BKT params | Code submission UI (Monaco editor + submit) |
| **D3** | SubmitAnswerHandler (CQRS pipeline) | Results display (correct/incorrect + feedback) |
| **D4** | Code Execution Service (Judge0 client) | Question navigation (prev/next, difficulty filter) |
| **D5** | AI Code Evaluation (Ollama analyzes code) | Progress tracking (mastery bars, completion %) |
| **D6** | Plagiarism Detection (CodeBERT + AST) | Dashboard updates (mastery heatmap, activity) |
| **D7** | Hangfire jobs + global query filter + testing | CI/CD updates, bug fixes, tag v1.2.0 |

---

## Day 1 — BKT Engine (Member A) + Practice Page (Member B)

### Member A: BKT Engine (Pure C#)

**What:** Implement `BktEngine.cs` — the Bayesian Knowledge Tracing math. Pure C#, no external dependencies, ~50 lines.

**Files to create:**
```
src/VoxMentor.Domain/Entities/BktParameters.cs
src/VoxMentor.Application/Services/BktEngine.cs
src/VoxMentor.Application/Services/IBktEngine.cs
```

**BktParameters entity:**
```csharp
// src/VoxMentor.Domain/Entities/BktParameters.cs
public class BktParameters
{
    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }
    public float PriorKnowledge { get; set; } = 0.1f;  // P(L₀)
    public float LearnRate { get; set; } = 3.0f;       // Learning rate multiplier
    public float GuessRate { get; set; } = 0.2f;       // P(correct | not learned)
    public float SlipRate { get; set; } = 0.1f;        // P(incorrect | learned)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**BktEngine.cs — the core math:**
```csharp
// src/VoxMentor.Application/Services/BktEngine.cs
public class BktEngine : IBktEngine
{
    // Update mastery after one observation (correct or incorrect)
    public float UpdateMastery(float currentMastery, BktParameters p, bool correct)
    {
        float pL = currentMastery;

        // P(correct) = P(C|L)*P(L) + P(C|~L)*P(~L)
        float pCorrect = (1 - p.SlipRate) * pL + p.GuessRate * (1 - pL);

        float newMastery;
        if (correct)
        {
            // P(L | correct) = P(C|L)*P(L) / P(C)
            newMastery = ((1 - p.SlipRate) * pL) / pCorrect;
        }
        else
        {
            // P(L | incorrect) = P(C|~L)*P(L) / P(~C)
            float pIncorrect = p.SlipRate * pL + (1 - p.GuessRate) * (1 - pL);
            newMastery = (p.SlipRate * pL) / pIncorrect;
        }

        // Apply learning rate (boost if correct, dampen if incorrect)
        if (correct)
            newMastery = newMastery + (1 - newMastery) * p.LearnRate * newMastery;
        else
            newMastery = newMastery * (1 - p.LearnRate * (1 - newMastery));

        return Math.Clamp(newMastery, 0f, 1f);
    }

    // Batch update: multiple observations
    public float UpdateMastery(float currentMastery, BktParameters p, IEnumerable<bool> observations)
    {
        float mastery = currentMastery;
        foreach (var correct in observations)
            mastery = UpdateMastery(mastery, p, correct);
        return mastery;
    }
}
```

**Key formulas (for reference):**
```
P(L₁ | correct) = P(C|L) × P(L) / P(C)
P(L₁ | incorrect) = P(C|~L) × P(L) / P(~C)

Where:
  P(C|L) = 1 - slip_rate
  P(C|~L) = guess_rate
  P(C) = P(C|L)×P(L) + P(C|~L)×P(~L)
  P(~C) = 1 - P(C)
```

**Unit tests:**
```csharp
// src/VoxMentor.Tests/Unit/BktEngineTests.cs
[Test]
public void UpdateMastery_CorrectAnswer_IncreasesMastery()
{
    var engine = new BktEngine();
    var p = new BktParameters { PriorKnowledge = 0.1f, LearnRate = 3.0f, GuessRate = 0.2f, SlipRate = 0.1f };
    float result = engine.UpdateMastery(0.1f, p, correct: true);
    Assert.That(result, Is.GreaterThan(0.1f));
}

[Test]
public void UpdateMastery_IncorrectAnswer_DecreasesMastery()
{
    var engine = new BktEngine();
    var p = new BktParameters { PriorKnowledge = 0.5f, LearnRate = 3.0f, GuessRate = 0.2f, SlipRate = 0.1f };
    float result = engine.UpdateMastery(0.5f, p, correct: false);
    Assert.That(result, Is.LessThan(0.5f));
}

[Test]
public void UpdateMastery_MasteryNeverExceedsOne()
{
    var engine = new BktEngine();
    var p = new BktParameters { PriorKnowledge = 0.9f, LearnRate = 3.0f, GuessRate = 0.2f, SlipRate = 0.1f };
    float result = engine.UpdateMastery(0.9f, p, correct: true);
    Assert.That(result, Is.LessThanOrEqualTo(1.0f));
}
```

**Verification:** Run `dotnet test src/VoxMentor.Tests` — all BKT tests green.

---

### Member B: Practice Page Skeleton

**What:** Build the practice page UI — question display area + code editor placeholder.

**Files to create/modify:**
```
web/app/practice/page.tsx
web/components/QuestionCard.tsx
web/components/CodeEditor.tsx
```

**Design specs:**
- Question card: shows question text, concept name, difficulty badge, example input/output
- Code editor: placeholder (Monaco integration on Day 2)
- Layout: question on left (60%), editor on right (40%), responsive stack on mobile
- Use existing design system (navy gradient, card components, Tailwind)

**Example skeleton:**
```tsx
// web/app/practice/page.tsx
"use client";
import ProtectedRoute from "@/components/ProtectedRoute";
import QuestionCard from "@/components/QuestionCard";
import CodeEditor from "@/components/CodeEditor";

export default function PracticePage() {
  // Placeholder state — will connect to API on Day 3
  const question = {
    text: "Given an array of integers, find the maximum sum of a contiguous subarray.",
    concept: "Dynamic Programming",
    difficulty: 6,
    examples: [
      { input: "[-2,1,-3,4,-1,2,1,-5,4]", output: "6" },
    ],
  };

  return (
    <ProtectedRoute>
      <div className="max-w-7xl mx-auto px-6 py-10">
        <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
          <div className="lg:col-span-3">
            <QuestionCard question={question} />
          </div>
          <div className="lg:col-span-2">
            <CodeEditor />
          </div>
        </div>
      </div>
    </ProtectedRoute>
  );
}
```

**Verification:** Page renders at `localhost:3000/practice`, shows question + editor layout.

---

## Day 2 — Mastery Entity + Migration (Member A) + Code Submission UI (Member B)

### Member A: Database Schema Changes

**What:** Add new columns/tables for mastery tracking and code submissions.

**Files to create/modify:**
```
src/VoxMentor.Domain/Entities/BktParameters.cs          (created Day 1)
src/VoxMentor.Domain/Entities/StudentMastery.cs          (add LastPracticedAt)
src/VoxMentor.Domain/Entities/CodeSubmission.cs           (add PlagiarismScore, AiEvaluation, Status)
src/VoxMentor.Infrastructure/Persistence/ApplicationDbContext.cs  (register new entities)
src/VoxMentor.Infrastructure/Migrations/                   (new migration)
```

**SQL changes (for reference):**
```sql
-- Add to StudentMastery
ALTER TABLE "StudentMastery" ADD COLUMN "LastPracticedAt" timestamp with time zone;

-- Add to CodeSubmissions
ALTER TABLE "CodeSubmissions" ADD COLUMN "PlagiarismScore" real;
ALTER TABLE "CodeSubmissions" ADD COLUMN "AiEvaluation" jsonb;
ALTER TABLE "CodeSubmissions" ADD COLUMN "Status" integer DEFAULT 0;

-- New table: BKT Parameters per concept
CREATE TABLE "BktParameters" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "ConceptId" uuid NOT NULL REFERENCES "Concepts"("Id"),
    "PriorKnowledge" real DEFAULT 0.1,
    "LearnRate" real DEFAULT 3.0,
    "GuessRate" real DEFAULT 0.2,
    "SlipRate" real DEFAULT 0.1,
    "CreatedAt" timestamp with time zone DEFAULT now(),
    "UpdatedAt" timestamp with time zone DEFAULT now()
);
```

**Steps:**
1. Update `StudentMastery.cs` — add `LastPracticedAt` property
2. Update `CodeSubmission.cs` — add `PlagiarismScore`, `AiEvaluation` (JSON), `Status` (enum)
3. Register `BktParameters` in `ApplicationDbContext`
4. Run `dotnet ef migrations add Week2_BktAndCodeEval --project src/VoxMentor.Infrastructure --startup-project src/VoxMentor.Api`
5. Verify migration generates correct SQL

**Seed BKT parameters for 50 concepts:**
```sql
-- scripts/seed-bkt-parameters.sql
INSERT INTO "BktParameters" ("Id", "ConceptId", "PriorKnowledge", "LearnRate", "GuessRate", "SlipRate", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), "Id", 0.1, 3.0, 0.2, 0.1, now(), now()
FROM "Concepts";
```

**Verification:** `dotnet ef database update` succeeds, BktParameters table has 50 rows.

---

### Member B: Code Submission UI

**What:** Add Monaco editor to the practice page with a submit button.

**Dependencies:** Install `@monaco-editor/react` (or use the existing Monaco from package.json if already there).

```bash
cd web && npm install @monaco-editor/react
```

**Files to create/modify:**
```
web/components/CodeEditor.tsx         (Monaco editor with language selector)
web/components/LanguageSelector.tsx   (Python/Java/C++/JS/C#/C picker)
```

**CodeEditor.tsx features:**
- Monaco editor with dark theme
- Language selector dropdown (python, java, cpp, javascript, csharp)
- "Run" / "Submit" button
- Loading spinner during submission
- Pre-filled template code per language (optional, nice-to-have)

**Verification:** Monaco editor loads, language selector works, code can be typed.

---

## Day 3 — Answer Submission Pipeline (Member A) + Results Display (Member B)

### Member A: SubmitAnswerCommand + Handler (CQRS)

**What:** Wire up the full answer submission flow: receive answer → load mastery → run BKT → persist → emit event.

**Files to create:**
```
src/VoxMentor.Application/Features/Practice/SubmitAnswer/SubmitAnswerCommand.cs
src/VoxMentor.Application/Features/Practice/SubmitAnswer/SubmitAnswerHandler.cs
src/VoxMentor.Application/Features/Practice/SubmitAnswer/SubmitAnswerValidator.cs
src/VoxMentor.Application/Features/Practice/SubmitAnswer/SubmitAnswerResultDto.cs
src/VoxMentor.Api/Controllers/AnswerController.cs
```

**SubmitAnswerCommand:**
```csharp
public record SubmitAnswerCommand : IRequest<ApiResponse<SubmitAnswerResultDto>>
{
    public Guid QuestionId { get; init; }
    public bool IsCorrect { get; init; }  // From Judge0 test results
}
```

**SubmitAnswerHandler (the pipeline):**
```csharp
public class SubmitAnswerHandler : IRequestHandler<SubmitAnswerCommand, ApiResponse<SubmitAnswerResultDto>>
{
    // 1. Get current user from JWT claims
    // 2. Load question → get ConceptId
    // 3. Load StudentMastery for (UserId, ConceptId) — create if doesn't exist
    // 4. Load BktParameters for ConceptId
    // 5. Run BktEngine.UpdateMastery(currentMastery, bktParams, isCorrect)
    // 6. Save new mastery + increment CorrectAttempts/IncorrectAttempts
    // 7. Set LastPracticedAt = now
    // 8. Persist to database
    // 9. Emit MasteryUpdated event (Redis Streams)
    // 10. Return result with mastery delta
}
```

**AnswerController:**
```csharp
[ApiController]
[Route("api/v1/answer")]
public class AnswerController : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> SubmitAnswer(
        [FromBody] SubmitAnswerCommand command)
    {
        var result = await _mediator.Send(command);
        return Ok(result);
    }
}
```

**Verification:**
- POST to `/api/v1/answer` with `{"questionId": "...", "isCorrect": true}`
- Returns `{"previousMastery": 0.1, "newMastery": 0.35, "masteryDelta": 0.25}`
- StudentMastery row updated in database

---

### Member B: Results Display

**What:** After code submission, show results to the user.

**Files to create/modify:**
```
web/components/ResultPanel.tsx
web/app/practice/page.tsx              (integrate ResultPanel)
```

**ResultPanel.tsx shows:**
- Test cases passed: 8/10
- Execution time: 45ms
- Memory usage: 12.3 MB
- AI evaluation scores (correctness, complexity, style)
- Mastery change: +0.07 (with animation)
- "Correct" green badge or "Incorrect" red badge

**Verification:** After submission, results panel appears with scores and mastery delta.

---

## Day 4 — Code Execution Service (Member A) + Question Navigation (Member B)

### Member A: Judge0 Integration

**What:** Set up the Code Execution Service with Judge0 client for sandboxed code execution.

**Files to create/modify:**
```
src/VoxMentor.CodeExecService/Clients/Judge0Client.cs
src/VoxMentor.CodeExecService/Services/CodeExecutionService.cs
src/VoxMentor.CodeExecService/Controllers/ExecuteController.cs
src/VoxMentor.CodeExecService/Models/ExecutionRequest.cs
src/VoxMentor.CodeExecService/Models/ExecutionResult.cs
```

**Judge0Client.cs:**
```csharp
public class Judge0Client
{
    private readonly HttpClient _http;
    private readonly string _baseUrl = "http://localhost:2358";

    // Language IDs:
    // Python = 71, Java = 62, C++ = 54, C = 50,
    // JavaScript = 63, C# = 51

    public async Task<ExecutionResult> ExecuteAsync(
        string sourceCode, int languageId, string stdin = "")
    {
        // POST /submissions
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/submissions", new
        {
            source_code = sourceCode,
            language_id = languageId,
            stdin = stdin,
            cpu_time_limit = 5,
            memory_limit = 256000
        });

        var submission = await response.Content
            .ReadFromJsonAsync<Judge0Submission>();

        // Poll until status.id >= 3 (finished)
        return await PollResultAsync(submission.Token);
    }
}
```

**ExecuteController:**
```csharp
[ApiController]
[Route("api/v1/execute")]
public class ExecuteController : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> ExecuteCode(
        [FromBody] ExecutionRequest request)
    {
        // 1. Validate language is supported
        // 2. Execute via Judge0 (with 10s timeout)
        // 3. Compare output vs expected test cases
        // 4. Return results (pass/fail per test case)
    }
}
```

**Verification:**
- POST Python code `print("hello")` → returns stdout: "hello"
- POST code with test cases → returns pass/fail per case
- Timeout handling for infinite loops

---

### Member B: Question Navigation

**What:** Add question list, prev/next navigation, and difficulty filter to the practice page.

**Files to create/modify:**
```
web/components/QuestionList.tsx
web/components/QuestionNavigation.tsx
web/app/practice/page.tsx              (integrate navigation)
```

**Features:**
- Question list sidebar (scrollable, shows concept + difficulty)
- Prev/Next buttons to move between questions
- Difficulty filter (Easy/Medium/Hard or 1-10 slider)
- Concept filter dropdown
- Question counter: "Question 3 of 100"
- Highlight current question in list

**Verification:** Can navigate between questions, filter by difficulty, see question count.

---

## Day 5 — AI Code Evaluation (Member A) + Progress Tracking (Member B)

### Member A: Ollama Code Evaluation

**What:** Use Ollama to analyze submitted code for correctness, complexity, and style.

**Files to create:**
```
src/VoxMentor.Infrastructure/Ai/OllamaCodeEvaluator.cs
src/VoxMentor.Application/Interfaces/ICodeEvaluator.cs
src/VoxMentor.Application/Models/CodeEvaluation.cs
```

**Evaluation prompt template:**
```csharp
public class OllamaCodeEvaluator : ICodeEvaluator
{
    public async Task<CodeEvaluation> EvaluateAsync(
        string code, string language, string expectedOutput)
    {
        var prompt = $@"Analyze this {language} code solution.

Code:
{code}

Expected output: {expectedOutput}

Rate on these dimensions (1-10 each):
1. Correctness: Does it solve the problem correctly?
2. Time Complexity: Is the time complexity optimal? State the Big-O.
3. Space Complexity: Is the space complexity optimal? State the Big-O.
4. Code Style: Naming, readability, documentation.

Return JSON:
{{
  ""correctness"": {{ ""score"": N, ""feedback"": ""..."" }},
  ""timeComplexity"": {{ ""score"": ""O(...)"", ""isOptimal"": true, ""feedback"": ""..."" }},
  ""spaceComplexity"": {{ ""score"": ""O(...)"", ""isOptimal"": true, ""feedback"": ""..."" }},
  ""codeStyle"": {{ ""score"": N, ""feedback"": ""..."" }}
}}";

        var response = await _ollamaClient.ChatAsync("llama3.2:3b", prompt);
        return ParseEvaluation(response);
    }
}
```

**Integration into submit flow:**
- After Judge0 execution, send code to Ollama for evaluation
- Store evaluation result in `CodeSubmissions.AiEvaluation` (JSON column)
- Return evaluation to frontend

**Verification:**
- Submit a correct solution → AI gives high scores
- Submit a brute-force solution → AI suggests optimization
- Submit wrong code → AI identifies the bug

---

### Member B: Progress Tracking UI

**What:** Show mastery progress across all concepts.

**Files to create:**
```
web/components/MasteryProgressBar.tsx
web/components/ConceptCard.tsx
web/app/practice/page.tsx              (add progress section)
```

**Features:**
- Mastery progress bars per concept (0% to 100%)
- Color coding: red (<30%), yellow (30-70%), green (>70%)
- "Mastered" badge for concepts with mastery ≥ 0.85
- Overall readiness score display
- Number of problems solved per concept

**Verification:** Progress bars render with sample data, colors are correct, mastered badge shows.

---

## Day 6 — Plagiarism Detection (Member A) + Dashboard Updates (Member B)

### Member A: CodeBERT + AST Comparison

**What:** Detect similar code submissions using embedding similarity + AST structure comparison.

**Files to create:**
```
src/VoxMentor.Infrastructure/Plagiarism/CodeEmbeddingService.cs
src/VoxMentor.Infrastructure/Plagiarism/AstComparator.cs
src/VoxMentor.Infrastructure/Plagiarism/PlagiarismDetector.cs
src/VoxMentor.Application/Interfaces/IPlagiarismDetector.cs
```

**CodeEmbeddingService:**
- Use Ollama `nomic-embed-text` to embed submitted code (768-dim vector)
- Store embedding in `TextbookChunks` table (or a new `CodeEmbeddings` table)
- Query for similar embeddings using pgvector cosine similarity

**AstComparator:**
- Parse code into AST using tree-sitter (via `TreeSitter` NuGet or CLI)
- Compare AST structure (tree edit distance)
- More reliable than text comparison for renamed variables

**PlagiarismDetector:**
```csharp
public class PlagiarismDetector : IPlagiarismDetector
{
    public async Task<PlagiarismResult> CheckAsync(
        string code, string language, Guid userId)
    {
        // 1. Embed code → vector
        var embedding = await _embeddingService.EmbedAsync(code);

        // 2. Find similar submissions (pgvector, threshold > 0.85)
        var similar = await _db.CodeSubmissions
            .Where(s => s.UserId != userId)
            .Where(s => PgVectorOperators.CosineDistance(s.Embedding, embedding) > 0.85)
            .ToListAsync();

        // 3. AST comparison for top matches
        var astScore = await _astComparator.CompareAsync(code, similar);

        // 4. Combined score (0.0 = original, 1.0 = identical)
        float score = Math.Max(vectorSimilarity, astScore);

        return new PlagiarismResult { Score = score, Matches = similar };
    }
}
```

**Verification:**
- Submit same code twice → plagiarism score > 0.9
- Submit modified code (renamed vars) → score > 0.7
- Submit original code → score < 0.3

---

### Member B: Dashboard Updates

**What:** Enhance the dashboard with mastery heatmap and recent activity.

**Files to create/modify:**
```
web/components/MasteryHeatmap.tsx
web/components/RecentActivity.tsx
web/app/dashboard/page.tsx              (integrate new components)
```

**MasteryHeatmap:**
- GitHub-style contribution grid
- X-axis: concepts (grouped by category)
- Y-axis: mastery level (color intensity)
- Hover shows concept name + mastery %

**RecentActivity:**
- List of recent practice sessions
- Shows: question solved, mastery change, timestamp
- "Last practiced: 2 hours ago"

**Verification:** Dashboard shows heatmap + activity feed with sample data.

---

## Day 7 — Hangfire Jobs + Polish + Tag v1.2.0

### Member A: Background Jobs

**What:** Set up Hangfire for nightly BKT tuning and spaced repetition.

**Files to create/modify:**
```
src/VoxMentor.Infrastructure/Jobs/BktParameterTuningJob.cs
src/VoxMentor.Infrastructure/Jobs/SpacedRepetitionDecayJob.cs
src/VoxMentor.Api/Program.cs                              (Hangfire setup)
```

**BktParameterTuningJob (EM algorithm):**
```csharp
// Runs nightly at 2 AM
// For each concept with 50+ submissions:
//   1. Collect (correct, mastery_before) pairs
//   2. Run EM algorithm to optimize p(slip), p(guess), p(learn)
//   3. Update BktParameters table
public class BktParameterTuningJob
{
    public async Task Execute()
    {
        var concepts = await _db.Concepts.ToListAsync();
        foreach (var concept in concepts)
        {
            var submissions = await _db.CodeSubmissions
                .Where(s => s.Question.ConceptId == concept.Id)
                .ToListAsync();

            if (submissions.Count < 50) continue;

            // EM optimization (simplified)
            var (slip, guess, learn) = RunEmAlgorithm(submissions);

            var bktParams = await _db.BktParameters
                .FirstAsync(b => b.ConceptId == concept.Id);
            bktParams.SlipRate = slip;
            bktParams.GuessRate = guess;
            bktParams.LearnRate = learn;
        }
        await _db.SaveChangesAsync();
    }
}
```

**SpacedRepetitionDecayJob:**
```csharp
// Runs nightly at 3 AM
// For each student mastery not practiced in 7+ days:
//   Decay mastery by 5% per day (half-life ~14 days)
public class SpacedRepetitionDecayJob
{
    public async Task Execute()
    {
        var staleMasteries = await _db.StudentMasteries
            .Where(m => m.LastPracticedAt < DateTime.UtcNow.AddDays(7))
            .ToListAsync();

        foreach (var m in staleMasteries)
        {
            var daysSince = (DateTime.UtcNow - m.LastPracticedAt).Days;
            var decay = Math.Pow(0.95, daysSince - 7); // 5% decay per day
            m.Mastery = Math.Max(0.1f, m.Mastery * (float)decay);
        }
        await _db.SaveChangesAsync();
    }
}
```

**Global Query Filter (multi-tenant isolation):**
```csharp
// In ApplicationDbContext.cs
protected override void OnModelCreating(ModelBuilder builder)
{
    // All user-scoped entities get automatic UserId filter
    foreach (var entityType in builder.Model.GetEntityTypes())
    {
        if (typeof(IUserOwned).IsAssignableFrom(entityType.ClrType))
        {
            builder.Entity(entityType.ClrType)
                .HasQueryFilter(CreateTenantFilter(entityType.ClrType));
        }
    }
}
```

**Verification:**
- Hangfire dashboard at `/hangfire` shows recurring jobs
- BKT tuning job runs and updates parameters
- Spaced repetition decay reduces stale mastery scores

---

### Member B: CI/CD + Final Polish

**What:** Update CI, fix bugs, tag release.

**Tasks:**
1. Update `.github/workflows/ci.yml`:
   - Add `dotnet test` with code coverage
   - Add `npm run lint` + `npm run build` for frontend
   - Add Docker build test
2. Fix any failing tests
3. Update `CHANGELOG.md` for v1.2.0
4. Tag release: `git tag -a v1.2.0 -m "Week 2: BKT engine, code execution, AI eval"`

**Verification:**
- CI passes on all PRs
- `CHANGELOG.md` updated
- Tag `v1.2.0` created

---

## Database Schema Changes Summary

| Change | Table | Details |
|---|---|---|
| Add column | `StudentMastery` | `LastPracticedAt` (timestamp) |
| Add columns | `CodeSubmissions` | `PlagiarismScore` (float), `AiEvaluation` (jsonb), `Status` (int) |
| New table | `BktParameters` | `Id`, `ConceptId`, `PriorKnowledge`, `LearnRate`, `GuessRate`, `SlipRate` |

---

## End-of-Week Checklist

### Backend (Member A)
- [ ] BKT engine updates mastery after each answer (unit tests pass)
- [ ] POST `/api/v1/answer` works end-to-end
- [ ] Code execution via Judge0 works (Python, Java, C++, JS, C#)
- [ ] AI evaluates code quality (correctness, complexity, style)
- [ ] Plagiarism detection flags similar submissions (>0.85 score)
- [ ] 100 practice questions seeded in database
- [ ] Hangfire jobs configured (BKT tuning + spaced repetition)
- [ ] Global query filter for multi-tenant isolation

### Frontend (Member B)
- [ ] Practice page shows question + Monaco editor
- [ ] Code submission sends to backend + shows results
- [ ] Mastery progress bars render per concept
- [ ] Dashboard shows mastery heatmap + recent activity
- [ ] Question navigation (prev/next, difficulty filter) works
- [ ] CI green on every PR

### Both
- [ ] All unit tests pass (`dotnet test`)
- [ ] Frontend builds (`npm run build`)
- [ ] `CHANGELOG.md` updated for v1.2.0
- [ ] Tag `v1.2.0` created on `main`
- [ ] Demo: register → upload JD → practice question → see mastery update

---

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Judge0 setup issues | Can't execute code | Use Docker, check ports, test with `print("hello")` first |
| Ollama slow responses | AI eval takes 10s+ | Use `llama3.2:3b` (smallest model), cache frequent evaluations |
| CodeBERT large download | Docker build slow | Pre-download in Dockerfile, use `nomic-embed-text` instead |
| BKT math errors | Wrong mastery scores | Validate with known parameters, add comprehensive unit tests |
| Monaco editor bundle size | Frontend slow to load | Use dynamic import (`next/dynamic`), load on practice page only |
| EF migration conflicts | Database broken | Always create migration from clean state, test up + down |

---

## Git Workflow

```bash
# Branch naming
feat/SCRUM-12-bkt-engine
feat/SCRUM-17-submit-answer
fix/SCRUM-20-judge0-timeout

# Commit messages
feat(SCRUM-12): implement BKT engine with 4-parameter Bayes rule
feat(SCRUM-17): add SubmitAnswerCommand CQRS handler
fix(SCRUM-20): increase Judge0 timeout from 5s to 10s

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
  P(guess) = probability of correct answer when not learned (default: 0.2)
  P(slip) = probability of incorrect answer when learned (default: 0.1)
  P(learn) = learning rate multiplier (default: 3.0)

After observation (correct/incorrect):
  P(C) = P(C|L)×P(L) + P(C|~L)×P(~L)
  
  If correct:
    P(L|correct) = P(C|L)×P(L) / P(C)
    New mastery = P(L|correct) + (1 - P(L|correct)) × learnRate × P(L|correct)
  
  If incorrect:
    P(L|incorrect) = P(C|~L)×P(L) / P(~C)
    New mastery = P(L|incorrect) × (1 - learnRate × (1 - P(L|incorrect)))

Mastery thresholds:
  < 0.3  = Not started
  0.3-0.7 = Learning
  0.7-0.85 = Almost mastered
  ≥ 0.85 = Mastered
```

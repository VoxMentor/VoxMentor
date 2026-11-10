using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoxMentor.Application.Features.MockInterviews;
using VoxMentor.Domain.Entities;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Infrastructure.Persistence.Seeders;

/// <summary>
/// Idempotent demo seed data for the compose stack.
/// Runs only when Seed:Demo=true (env/config) or --seed-demo CLI arg is passed.
/// Safe to run repeatedly — inserts only if row not found by deterministic ID or natural key.
/// </summary>
public static class DemoSeeder
{
    // Deterministic demo identities (GUIDv5-style namespace strings)
    private static readonly Guid StudentId = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminId = Guid.Parse("d0000000-0000-0000-0000-000000000002");

    // Demo JD IDs
    private static readonly Guid JdAmazonId = Guid.Parse("d0000000-0000-0000-0000-000000000011");
    private static readonly Guid JdGoogleId = Guid.Parse("d0000000-0000-0000-0000-000000000012");

    // Demo interview IDs
    private static readonly Guid InterviewTechId = Guid.Parse("d0000000-0000-0000-0000-000000000021");
    private static readonly Guid InterviewBehavId = Guid.Parse("d0000000-0000-0000-0000-000000000022");

    // Demo tutor session IDs
    private static readonly Guid Tutor1Id = Guid.Parse("d0000000-0000-0000-0000-000000000031");
    private static readonly Guid Tutor2Id = Guid.Parse("d0000000-0000-0000-0000-000000000032");
    private static readonly Guid Tutor3Id = Guid.Parse("d0000000-0000-0000-0000-000000000033");

    // Demo skill-weight IDs (deterministic, one per (JD, skill) pair)
    private static readonly Guid[] JdAmazonSkillWeightIds =
    {
        Guid.Parse("d0000000-0000-0000-0000-000000000121"), // Distributed Systems
        Guid.Parse("d0000000-0000-0000-0000-000000000122"), // System Design
        Guid.Parse("d0000000-0000-0000-0000-000000000123"), // Algorithms
        Guid.Parse("d0000000-0000-0000-0000-000000000124"), // Leadership Principles
        Guid.Parse("d0000000-0000-0000-0000-000000000125"), // Java/Python/C++
    };
    private static readonly Guid[] JdGoogleSkillWeightIds =
    {
        Guid.Parse("d0000000-0000-0000-0000-000000000126"), // System Design
        Guid.Parse("d0000000-0000-0000-0000-000000000127"), // Algorithms
        Guid.Parse("d0000000-0000-0000-0000-000000000128"), // Googliness
        Guid.Parse("d0000000-0000-0000-0000-000000000129"), // Data Structures
        Guid.Parse("d0000000-0000-0000-0000-000000000130"), // Collaboration
    };

    // Demo mastery IDs
    private static readonly Guid[] MasteryIds =
    {
        Guid.Parse("d0000000-0000-0000-0000-000000000101"),
        Guid.Parse("d0000000-0000-0000-0000-000000000102"),
        Guid.Parse("d0000000-0000-0000-0000-000000000103"),
        Guid.Parse("d0000000-0000-0000-0000-000000000104"),
        Guid.Parse("d0000000-0000-0000-0000-000000000105"),
        Guid.Parse("d0000000-0000-0000-0000-000000000106"),
        Guid.Parse("d0000000-0000-0000-0000-000000000107"),
        Guid.Parse("d0000000-0000-0000-0000-000000000108"),
        Guid.Parse("d0000000-0000-0000-0000-000000000109"),
        Guid.Parse("d0000000-0000-0000-0000-000000000110"),
    };

    // Existing concept GUIDs from scripts/seed-dsa-concepts.sql (a0000001-...)
    private static readonly Guid[] ExistingConceptIds =
    {
        Guid.Parse("a0000001-0000-0000-0000-000000000001"), // Variables and Data Types
        Guid.Parse("a0000001-0000-0000-0000-000000000002"), // Control Flow
        Guid.Parse("a0000001-0000-0000-0000-000000000003"), // Functions
        Guid.Parse("a0000001-0000-0000-0000-000000000004"), // Time Complexity
        Guid.Parse("a0000001-0000-0000-0000-000000000005"), // Space Complexity
        Guid.Parse("a0000001-0000-0000-0000-000000000006"), // Arrays
        Guid.Parse("a0000001-0000-0000-0000-000000000007"), // Strings
        Guid.Parse("a0000001-0000-0000-0000-000000000008"), // Singly Linked Lists
        Guid.Parse("a0000001-0000-0000-0000-000000000009"), // Stacks and Queues
        Guid.Parse("a0000001-0000-0000-0000-000000000010"), // Hash Maps and Sets
    };

    // Fixed base time for reproducible timestamps across re-runs.
    private static readonly DateTime SeedBaseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedDemoDataAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        // Gate: require Seed:Demo=true (defensive bool parse). Returns no-op when unset.
        var gateEnabled = IsDemoSeedEnabled(configuration.GetValue<string>("Seed:Demo"));
        if (!gateEnabled)
        {
            return; // Prod-safe no-op
        }

        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<VoxMentor.Infrastructure.Persistence.ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Password must be configured when seeding is requested — never ship a well-known demo credential.
        var demoPassword = configuration.GetValue<string>("Seed:DemoPassword");
        if (string.IsNullOrEmpty(demoPassword))
            throw new InvalidOperationException(
                "Seed:DemoPassword must be configured when Seed:Demo=true. " +
                "Set it via env, appsettings.Development.json, or Secret Manager.");

        // Fail fast before writing anything: demo data builds on Step 4 SQL seeds (concepts).
        // IgnoreQueryFilters for consistency — Concept has no global filter currently,
        // but this protects against future changes adding one.
        var existingCount = await db.Concepts
            .IgnoreQueryFilters()
            .CountAsync(c => ExistingConceptIds.Contains(c.Id));
        if (existingCount < ExistingConceptIds.Length)
        {
            throw new InvalidOperationException(
                $"Demo seed requires Step 4 SQL seeds (concepts) to be present first. " +
                $"Found {existingCount}/{ExistingConceptIds.Length} concepts. Run SETUP.md Step 4 first.");
        }

        // ── Demo Users ───────────────────────────────────────────────────────
        var student = await userManager.FindByEmailAsync("demo-student@voxmentor.dev");
        if (student is null)
        {
            student = new ApplicationUser
            {
                Id = StudentId.ToString(), // IdentityUser<string> PK
                UserName = "demo-student@voxmentor.dev",
                Email = "demo-student@voxmentor.dev",
                EmailConfirmed = true,
                FullName = "Demo Student",
                CreatedAt = SeedBaseTime,
            };
            var result = await userManager.CreateAsync(student, demoPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create demo student: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            await userManager.AddToRoleAsync(student, "Student");
        }

        // Ensure roles are assigned even when the user already existed
        if (!await userManager.IsInRoleAsync(student, "Student"))
        {
            var roleResult = await userManager.AddToRoleAsync(student, "Student");
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Failed to assign Student role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }

        var admin = await userManager.FindByEmailAsync("demo-admin@voxmentor.dev");
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = AdminId.ToString(),
                UserName = "demo-admin@voxmentor.dev",
                Email = "demo-admin@voxmentor.dev",
                EmailConfirmed = true,
                FullName = "Demo Admin",
                CreatedAt = SeedBaseTime,
            };
            var result = await userManager.CreateAsync(admin, demoPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create demo admin: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            await userManager.AddToRoleAsync(admin, "ContentAdmin");
        }

        if (!await userManager.IsInRoleAsync(admin, "ContentAdmin"))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, "ContentAdmin");
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Failed to assign ContentAdmin role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }

        // ── Job Descriptions ─────────────────────────────────────────────────
        var jdAmazon = await db.JobDescriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == JdAmazonId);
        if (jdAmazon is null)
        {
            jdAmazon = new JobDescription
            {
                Id = JdAmazonId,
                UserId = student.Id,
                CompanyName = "Amazon",
                Role = "Software Development Engineer II",
                RawText = "Amazon SDE II role. Design and build scalable distributed systems. Strong ownership, customer obsession, and bias for action required. 3+ years experience with Java/Python/C++, system design, and algorithms.",
                Difficulty = "Hard",
                EstimatedWeeks = 4,
                CreatedAt = SeedBaseTime,
            };
            db.JobDescriptions.Add(jdAmazon);
        }

        var jdGoogle = await db.JobDescriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == JdGoogleId);
        if (jdGoogle is null)
        {
            jdGoogle = new JobDescription
            {
                Id = JdGoogleId,
                UserId = student.Id,
                CompanyName = "Google",
                Role = "Software Engineer, Level 4",
                RawText = "Google SWE L4. Build next-gen products at massive scale. Googliness: collaboration, user focus, comfort with ambiguity. Strong algorithms, data structures, and system design. 2+ years experience.",
                Difficulty = "Hard",
                EstimatedWeeks = 4,
                CreatedAt = SeedBaseTime,
            };
            db.JobDescriptions.Add(jdGoogle);
        }

        // JD Skill Weights
        await UpsertJdSkillWeightAsync(db, JdAmazonId, JdAmazonSkillWeightIds[0], "Distributed Systems", 0.30f, true);
        await UpsertJdSkillWeightAsync(db, JdAmazonId, JdAmazonSkillWeightIds[1], "System Design", 0.25f, true);
        await UpsertJdSkillWeightAsync(db, JdAmazonId, JdAmazonSkillWeightIds[2], "Algorithms", 0.20f, true);
        await UpsertJdSkillWeightAsync(db, JdAmazonId, JdAmazonSkillWeightIds[3], "Leadership Principles", 0.15f, false);
        await UpsertJdSkillWeightAsync(db, JdAmazonId, JdAmazonSkillWeightIds[4], "Java/Python/C++", 0.10f, true);

        await UpsertJdSkillWeightAsync(db, JdGoogleId, JdGoogleSkillWeightIds[0], "System Design", 0.30f, true);
        await UpsertJdSkillWeightAsync(db, JdGoogleId, JdGoogleSkillWeightIds[1], "Algorithms", 0.25f, true);
        await UpsertJdSkillWeightAsync(db, JdGoogleId, JdGoogleSkillWeightIds[2], "Googliness", 0.20f, false);
        await UpsertJdSkillWeightAsync(db, JdGoogleId, JdGoogleSkillWeightIds[3], "Data Structures", 0.15f, true);
        await UpsertJdSkillWeightAsync(db, JdGoogleId, JdGoogleSkillWeightIds[4], "Collaboration", 0.10f, false);

        // ── Student Mastery (BKT) ─────────────────────────────────────────────

        var masteryData = new (Guid Id, Guid ConceptId, float Mastery, int Correct, int Incorrect, DateTime LastPracticed)[]
        {
            (MasteryIds[0], ExistingConceptIds[0], 0.92f, 8, 1, SeedBaseTime.AddDays(-2)),   // Variables and Data Types
            (MasteryIds[1], ExistingConceptIds[1], 0.87f, 7, 2, SeedBaseTime.AddDays(-3)),   // Control Flow
            (MasteryIds[2], ExistingConceptIds[2], 0.78f, 6, 3, SeedBaseTime.AddDays(-5)),   // Functions
            (MasteryIds[3], ExistingConceptIds[3], 0.65f, 5, 4, SeedBaseTime.AddDays(-7)),   // Time Complexity
            (MasteryIds[4], ExistingConceptIds[4], 0.58f, 4, 5, SeedBaseTime.AddDays(-10)),  // Space Complexity
            (MasteryIds[5], ExistingConceptIds[5], 0.81f, 7, 2, SeedBaseTime.AddDays(-4)),   // Arrays
            (MasteryIds[6], ExistingConceptIds[6], 0.73f, 6, 3, SeedBaseTime.AddDays(-6)),   // Strings
            (MasteryIds[7], ExistingConceptIds[7], 0.48f, 3, 6, SeedBaseTime.AddDays(-14)),  // Linked Lists
            (MasteryIds[8], ExistingConceptIds[8], 0.55f, 4, 5, SeedBaseTime.AddDays(-12)),  // Stacks and Queues
            (MasteryIds[9], ExistingConceptIds[9], 0.62f, 5, 4, SeedBaseTime.AddDays(-9)),   // Hash Maps
        };

        foreach (var (id, conceptId, mastery, correct, incorrect, lastPracticed) in masteryData)
        {
            var m = await db.StudentMasteries
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (m is null)
            {
                m = new StudentMastery
                {
                    Id = id,
                    UserId = student.Id,
                    ConceptId = conceptId,
                    MasteryProbability = mastery,
                    CorrectAttempts = correct,
                    IncorrectAttempts = incorrect,
                    LastPracticedAt = lastPracticed,
                    CreatedAt = SeedBaseTime.AddDays(-30),
                    UpdatedAt = lastPracticed,
                };
                db.StudentMasteries.Add(m);
            }
        }

        // ── Mock Interviews ──────────────────────────────────────────────────
        // Technical interview (Amazon, completed, with transcript + review)
        var techBase = SeedBaseTime.AddHours(-1);
        var interviewTech = await db.MockInterviews
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == InterviewTechId);
        if (interviewTech is null)
        {
            var turnsTech = new List<TranscriptTurn>
            {
                new(TranscriptTurn.InterviewerRole, "Welcome to your Amazon SDE II mock interview. Let's start with a system design question.", Guid.Parse("b0000001-0000-0000-0000-000000000005"), techBase.AddMinutes(-45)), // Arrays question
                new(TranscriptTurn.CandidateRole, "Sure, I'd be happy to discuss. Let me first clarify the requirements...", Guid.Parse("b0000001-0000-0000-0000-000000000005"), techBase.AddMinutes(-42)),
                new(TranscriptTurn.InterviewerRole, "Good. Now let's move to an algorithms problem — implement a LRU cache.", Guid.Parse("b0000001-0000-0000-0000-000000000006"), techBase.AddMinutes(-30)), // Strings question
                new(TranscriptTurn.CandidateRole, "I'll use a hash map + doubly linked list for O(1) get and put...", Guid.Parse("b0000001-0000-0000-0000-000000000006"), techBase.AddMinutes(-25)),
                new(TranscriptTurn.InterviewerRole, "Excellent. Final question: tell me about a time you had to make a trade-off between speed and quality.", null, techBase.AddMinutes(-15)), // Behavioral
                new(TranscriptTurn.CandidateRole, "In my last project, we had a critical bug before launch...", null, techBase.AddMinutes(-10)),
            };

            var reviewTech = new MockInterviewReview(
                Score: 82f,
                Type: "Technical",
                TechnicalReview: new Dictionary<string, ProblemReview>(StringComparer.Ordinal)
                {
                    ["problem1"] = new ProblemReview(true, "Optimal", "Strong system design discussion — identified sharding strategy, consistent hashing, and failure modes."),
                    ["problem2"] = new ProblemReview(true, "Optimal", "Clean LRU implementation with proper O(1) operations. Good edge-case handling for capacity=0."),
                    ["problem3"] = new ProblemReview(true, "Good", "STAR-structured behavioral answer showing ownership and bias for action. Could quantify impact more."),
                },
                ReadinessDelta: new ReadinessDeltaDto(68f, 74f, 6f),
                NextSteps: "Strong technical performance. Practice quantifying behavioral impact with metrics. Review advanced distributed systems patterns for L6 readiness."
            );

            interviewTech = new MockInterview
            {
                Id = InterviewTechId,
                UserId = student.Id,
                Type = InterviewType.Technical,
                TotalQuestions = 3,
                AnsweredQuestions = 3,
                Score = 82f,
                Feedback = reviewTech.NextSteps,
                StartedAt = techBase.AddMinutes(-50),
                CompletedAt = techBase.AddMinutes(-5),
                JdId = JdAmazonId,
                Status = MockInterviewStatus.Completed,
                TranscriptJson = JsonSerializer.Serialize(turnsTech, TranscriptTurn.JsonOptions),
                VoiceMode = false,
                ReviewJson = JsonSerializer.Serialize(reviewTech, TranscriptTurn.JsonOptions),
            };
            db.MockInterviews.Add(interviewTech);
        }

        // Behavioral interview (no JD, completed)
        var behavBase = SeedBaseTime;
        var interviewBehav = await db.MockInterviews
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == InterviewBehavId);
        if (interviewBehav is null)
        {
            var turnsBehav = new List<TranscriptTurn>
            {
                new(TranscriptTurn.InterviewerRole, "Tell me about a challenging project you led.", null, behavBase.AddMinutes(-25)),
                new(TranscriptTurn.CandidateRole, "I led the migration of our payment service to a new provider...", null, behavBase.AddMinutes(-20)),
                new(TranscriptTurn.InterviewerRole, "Describe a time you disagreed with a team decision.", null, behavBase.AddMinutes(-15)),
                new(TranscriptTurn.CandidateRole, "We had a debate about API versioning strategy...", null, behavBase.AddMinutes(-10)),
                new(TranscriptTurn.InterviewerRole, "How do you handle conflicting priorities from stakeholders?", null, behavBase.AddMinutes(-5)),
                new(TranscriptTurn.CandidateRole, "I use a prioritization framework based on impact and urgency...", null, behavBase),
            };

            var reviewBehav = new MockInterviewReview(
                Score: 76f,
                Type: "Behavioral",
                TechnicalReview: new Dictionary<string, ProblemReview>(StringComparer.Ordinal)
                {
                    ["problem1"] = new ProblemReview(true, "Good", "Clear STAR structure. Demonstrated ownership and technical depth in migration challenges."),
                    ["problem2"] = new ProblemReview(true, "Good", "Showed collaboration and bias for action. Could elaborate more on the data-driven decision."),
                    ["problem3"] = new ProblemReview(true, "Acceptable", "Reasonable framework. Would benefit from a concrete example with trade-off analysis."),
                },
                ReadinessDelta: new ReadinessDeltaDto(74f, 77f, 3f),
                NextSteps: "Behavioral answers are structured well. Add more quantified outcomes and practice Amazon Leadership Principle vocabulary for SDE roles."
            );

            interviewBehav = new MockInterview
            {
                Id = InterviewBehavId,
                UserId = student.Id,
                Type = InterviewType.Behavioral,
                TotalQuestions = 3,
                AnsweredQuestions = 3,
                Score = 76f,
                Feedback = reviewBehav.NextSteps,
                StartedAt = behavBase.AddMinutes(-30),
                CompletedAt = behavBase,
                JdId = null,
                Status = MockInterviewStatus.Completed,
                TranscriptJson = JsonSerializer.Serialize(turnsBehav, TranscriptTurn.JsonOptions),
                VoiceMode = false,
                ReviewJson = JsonSerializer.Serialize(reviewBehav, TranscriptTurn.JsonOptions),
            };
            db.MockInterviews.Add(interviewBehav);
        }

        // ── Tutor Sessions ──────────────────────────────────────────────────
        await UpsertTutorSessionAsync(db, Tutor1Id, student.Id, ExistingConceptIds[0],
            "Can you explain the difference between pass-by-value and pass-by-reference in C#?",
            "In C#, **pass-by-value** (default for structs) copies the value. **Pass-by-reference** (using `ref`/`out` or passing a reference type) passes the reference, so changes affect the original. Reference types (classes) are passed by value of the reference — you can mutate the object but not reassign the parameter itself without `ref`.",
            TutorSessionStatus.Completed, 342, SeedBaseTime.AddDays(-8));

        await UpsertTutorSessionAsync(db, Tutor2Id, student.Id, ExistingConceptIds[3],
            "What's the time complexity of merge sort and why?",
            "Merge sort is **O(n log n)** in all cases (best/average/worst). It divides the array recursively (log n levels) and merges at each level (O(n) work per level). Space complexity is O(n) for the auxiliary array. Unlike quicksort, it doesn't degrade to O(n²) because the split is always balanced.",
            TutorSessionStatus.Completed, 287, SeedBaseTime.AddDays(-5));

        await UpsertTutorSessionAsync(db, Tutor3Id, student.Id, ExistingConceptIds[8],
            "How do you detect a cycle in a linked list?",
            "Use **Floyd's Tortoise and Hare** (two-pointer) algorithm: one pointer moves 1 step, the other 2 steps. If there's a cycle, they'll meet inside the cycle. Time O(n), space O(1). To find the cycle start, reset one pointer to head and advance both 1 step at a time — they meet at the cycle entry.",
            TutorSessionStatus.Completed, 415, SeedBaseTime.AddDays(-2));

        await db.SaveChangesAsync();
    }

    private static async Task UpsertJdSkillWeightAsync(
        VoxMentor.Infrastructure.Persistence.ApplicationDbContext db,
        Guid jdId, Guid id, string skillName, float weight, bool isTechnical)
    {
        var existing = await db.JdSkillWeights
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.JobDescriptionId == jdId && w.SkillName == skillName);
        if (existing is null)
        {
            db.JdSkillWeights.Add(new JdSkillWeight
            {
                Id = id,
                JobDescriptionId = jdId,
                SkillName = skillName,
                Weight = weight,
                IsTechnical = isTechnical,
                CreatedAt = SeedBaseTime,
            });
        }
    }

    private static async Task UpsertTutorSessionAsync(
        VoxMentor.Infrastructure.Persistence.ApplicationDbContext db,
        Guid id, string userId, Guid conceptId,
        string question, string answer,
        TutorSessionStatus status, int tokens, DateTime completedAt)
    {
        var existing = await db.TutorSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing is null)
        {
            db.TutorSessions.Add(new TutorSession
            {
                Id = id,
                UserId = userId,
                ConceptId = conceptId,
                Question = question,
                Answer = answer,
                Status = status,
                TotalTokens = tokens,
                CreatedAt = completedAt.AddMinutes(-5),
                StartedAt = completedAt.AddMinutes(-3),
                QueuedAt = completedAt.AddMinutes(-4),
                CompletedAt = completedAt,
            });
        }
    }

    /// <summary>
    /// Defensive bool parse for Seed:Demo config. Accepts true/1/yes/on (case-insensitive).
    /// Anything else (missing, empty, false/0/no/off, garbage) is treated as disabled — never crashes.
    /// </summary>
    internal static bool IsDemoSeedEnabled(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
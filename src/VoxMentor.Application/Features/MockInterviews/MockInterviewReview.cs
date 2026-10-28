namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>One problem's rubric verdict inside a mock-interview review (#125).</summary>
public record ProblemReview(bool Solved, string Complexity, string Feedback);

/// <summary>Readiness score movement caused by completing the interview (#125).</summary>
public record ReadinessDeltaDto(float Before, float After, float Delta);

/// <summary>
/// The persisted review payload (MockInterview.ReviewJson) and the 200 body of
/// POST /api/v1/mock/{id}/complete, per docs/API.md §6.
/// </summary>
public record MockInterviewReview(
    float Score,
    string Type,
    Dictionary<string, ProblemReview> TechnicalReview,
    ReadinessDeltaDto ReadinessDelta,
    string NextSteps);

/// <summary>
/// Rubric output straight from the model — no readiness fields, the handler
/// computes those. Null from the generator means "degrade to canned review".
/// </summary>
public record MockInterviewReviewDraft(
    float Score,
    Dictionary<string, ProblemReview> TechnicalReview,
    string NextSteps);

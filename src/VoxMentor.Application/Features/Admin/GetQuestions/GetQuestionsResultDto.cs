namespace VoxMentor.Application.Features.Admin.GetQuestions;

/// <summary>
/// Paginated list of questions for admin curation.
/// </summary>
/// <param name="Questions">The questions on this page.</param>
/// <param name="TotalCount">Total number of questions matching the query.</param>
/// <param name="Page">Current page number.</param>
/// <param name="PageSize">Items per page.</param>
public record GetQuestionsResultDto(
    List<QuestionDto> Questions,
    int TotalCount,
    int Page,
    int PageSize
);

/// <summary>
/// Summary of a question for list view.
/// </summary>
public record QuestionDto(
    Guid Id,
    Guid ConceptId,
    string Title,
    string Description,
    int Difficulty,
    int TestCaseCount,
    DateTime CreatedAt
);

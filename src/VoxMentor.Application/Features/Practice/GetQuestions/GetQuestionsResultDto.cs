namespace VoxMentor.Application.Features.Practice.GetQuestions;

/// <summary>Paginated list of practice questions for students.</summary>
public record GetQuestionsResultDto(
    List<StudentQuestionDto> Questions,
    int TotalCount,
    int Page,
    int PageSize
);

/// <summary>Summary of a question for the student question list.</summary>
public record StudentQuestionDto(
    Guid Id,
    Guid ConceptId,
    string ConceptName,
    string Title,
    string Description,
    string QuestionType,
    int Difficulty,
    int ExampleCount,
    int TotalTestCases,
    int HiddenTestCaseCount
);

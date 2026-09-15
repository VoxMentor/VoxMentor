namespace VoxMentor.Application.Features.Practice.GetQuestionById;

/// <summary>Full detail of a practice question with hidden test cases excluded.</summary>
public record QuestionDetailDto(
    Guid Id,
    Guid ConceptId,
    string ConceptName,
    string Title,
    string Description,
    string QuestionType,
    int Difficulty,
    string[] ExampleInputs,
    string[] ExampleOutputs,
    string[] StarterCode,
    string[] VisibleTestCases,
    string[] Rubric,
    int TotalTestCases,
    int HiddenTestCaseCount
);

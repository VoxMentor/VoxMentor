namespace VoxMentor.Application.Features.Practice.GetNextQuestion;

/// <summary>The next question selected by the adaptive selector.</summary>
public record NextQuestionDto(
    Guid QuestionId,
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
    string[] Rubric
);

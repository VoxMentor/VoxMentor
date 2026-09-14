namespace VoxMentor.Application.Features.Admin.CreateQuestion;

/// <summary>
/// Result of creating a new question in the bank.
/// </summary>
/// <param name="QuestionId">The created question's ID.</param>
/// <param name="ConceptId">The concept the question belongs to.</param>
/// <param name="Title">Question title.</param>
/// <param name="Difficulty">Difficulty level (1-10).</param>
public record CreateQuestionResultDto(
    Guid QuestionId,
    Guid ConceptId,
    string Title,
    int Difficulty
);

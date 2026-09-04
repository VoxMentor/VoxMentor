namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

/// <summary>
/// Result of an answer submission: mastery transition and updated attempt totals
/// for the question's concept.
/// </summary>
/// <param name="QuestionId">The question that was answered.</param>
/// <param name="ConceptId">The concept the question belongs to.</param>
/// <param name="IsCorrect">Whether the answer was correct.</param>
/// <param name="PreviousMastery">Mastery probability before applying this answer.</param>
/// <param name="NewMastery">Mastery probability after applying this answer.</param>
/// <param name="MasteryDelta"><paramref name="NewMastery"/> minus <paramref name="PreviousMastery"/>.</param>
/// <param name="CorrectAttempts">Total correct attempts after this submission.</param>
/// <param name="IncorrectAttempts">Total incorrect attempts after this submission.</param>
public record SubmitAnswerResultDto(
    Guid QuestionId,
    Guid ConceptId,
    bool IsCorrect,
    float PreviousMastery,
    float NewMastery,
    float MasteryDelta,
    int CorrectAttempts,
    int IncorrectAttempts
);

namespace VoxMentor.Application.Features.Practice.SubmitAnswer;

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

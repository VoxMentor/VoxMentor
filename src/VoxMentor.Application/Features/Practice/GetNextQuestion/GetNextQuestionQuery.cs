using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Practice.GetNextQuestion;

/// <summary>
/// Selects the next adaptive question for the student. Targets the weakest
/// concept at difficulty 1 + mastery×9, or a specific concept when
/// <see cref="ConceptId"/> is provided.
/// </summary>
public class GetNextQuestionQuery : IRequest<ApiResponse<NextQuestionDto>>
{
    /// <summary>Optional concept to practice; defaults to the weakest concept.</summary>
    public Guid? ConceptId { get; set; }
}

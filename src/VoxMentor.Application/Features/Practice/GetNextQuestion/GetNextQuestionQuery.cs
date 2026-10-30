using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

    /// <summary>
    /// Optional JD whose skill weights drive the ε-greedy selector (#124);
    /// null keeps the pre-existing "latest JD" behaviour. Ownership is the
    /// IUserOwned global filter — an unknown/foreign id resolves to no JD.
    /// </summary>
    public Guid? JdId { get; set; }

    /// <summary>
    /// Question ids the caller already served and never wants back (#148: a
    /// mock interview avoids repeats — an older problem may repeat only when
    /// the pool runs dry, never the one just answered when the pool allows);
    /// unioned with the student's attempted submissions before selection.
    /// Internal to the selector — never settable from an HTTP request.
    /// </summary>
    [BindNever]
    public IReadOnlyCollection<Guid>? ExcludeQuestionIds { get; set; }
}

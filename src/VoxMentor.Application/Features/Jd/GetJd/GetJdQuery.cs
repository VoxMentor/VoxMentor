using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Jd.GetJd;

/// <summary>
/// Parsed JD + readiness score + gaps + week-by-week roadmap for the current
/// student (#118).
/// </summary>
/// <param name="JdId">JD to fetch; 404 if missing or not owned by the caller.</param>
public record GetJdQuery(Guid JdId) : IRequest<ApiResponse<GetJdResultDto>>;

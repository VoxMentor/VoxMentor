using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.GetTextbookJob;

/// <summary>Polls a textbook ingestion job's progress.</summary>
public record GetTextbookJobQuery(Guid JobId) : IRequest<ApiResponse<TextbookJobDto>>;

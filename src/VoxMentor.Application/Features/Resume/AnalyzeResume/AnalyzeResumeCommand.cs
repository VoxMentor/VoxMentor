using MediatR;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Common.Interfaces;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>Analyzes an uploaded resume against the skills of a parsed JD.</summary>
public sealed record AnalyzeResumeCommand(
    string FileName,
    Stream Content,
    long Length,
    Guid JdId) : IRequest<ApiResponse<AnalyzeResumeResultDto>>;

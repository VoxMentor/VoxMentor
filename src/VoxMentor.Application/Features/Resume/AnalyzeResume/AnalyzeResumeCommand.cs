using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Resume.AnalyzeResume;

/// <summary>
/// Analyzes an uploaded resume (.pdf) against a JD's weighted skills and
/// returns an ATS fit report (#151).
/// </summary>
/// <param name="FileName">Original filename including extension.</param>
/// <param name="Content">Open stream of the uploaded file.</param>
/// <param name="Length">Declared file size in bytes (validated before reading).</param>
/// <param name="JdId">Job description to match against (required).</param>
public record AnalyzeResumeCommand(
    string FileName,
    Stream Content,
    long Length,
    Guid JdId
) : IRequest<ApiResponse<AnalyzeResumeResultDto>>;

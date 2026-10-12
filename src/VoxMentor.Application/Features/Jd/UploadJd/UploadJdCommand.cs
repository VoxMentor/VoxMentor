using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Jd.UploadJd;

/// <summary>
/// Uploads a job description for Ollama parsing with keyword-heuristic
/// fallback (#117). Persists the JD and its skill weights.
/// </summary>
/// <param name="RawText">Raw job-description text (50–20000 chars, validated).</param>
public record UploadJdCommand(string RawText) : IRequest<ApiResponse<UploadJdResultDto>>;

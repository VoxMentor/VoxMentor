using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>Starts an AI mock interview, optionally scoped to a job description (#124).</summary>
public class StartMockInterviewCommand : IRequest<ApiResponse<StartMockInterviewResultDto>>
{
    /// <summary>Job description to scope the interview to; optional (#124).</summary>
    public Guid? JdId { get; set; }

    /// <summary>Interview format; case-insensitive <c>Technical</c> or <c>Behavioral</c>, defaults to Technical.</summary>
    public string? Type { get; set; } = "Technical";

    /// <summary>Whether the caller intends to use voice mode; recorded by the client only (#129/#126).</summary>
    public bool VoiceMode { get; set; }
}

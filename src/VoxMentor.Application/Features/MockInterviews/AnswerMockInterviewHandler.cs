using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Interfaces;
using VoxMentor.Application.Common.Models;
using VoxMentor.Application.Common.Services;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.MockInterviews;

/// <summary>
/// POST /api/v1/mock/{interviewId}/answer (#124): appends the candidate's turn
/// to TranscriptJson, has Ollama produce the next probe, appends that turn too
/// (order preserved), and reports the new turn count. 404 for unknown/foreign
/// ids (IUserOwned filter), 409 once the run has ended.
/// </summary>
public class AnswerMockInterviewHandler : IRequestHandler<AnswerMockInterviewCommand, ApiResponse<AnswerMockInterviewResultDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IMockInterviewGenerator _generator;
    private readonly AsyncKeyedLock _lock;

    /// <summary>Initializes the handler with the data context, the Ollama dialogue generator, and the lock.</summary>
    public AnswerMockInterviewHandler(IApplicationDbContext db, IMockInterviewGenerator generator, AsyncKeyedLock @lock)
    {
        _db = db;
        _generator = generator;
        _lock = @lock;
    }

    /// <summary>Appends both turns, persists the transcript, and returns the interviewer's message.</summary>
    public async Task<ApiResponse<AnswerMockInterviewResultDto>> Handle(
        AnswerMockInterviewCommand request, CancellationToken cancellationToken)
    {
        // #124 concurrency (CodeRabbit #146): serialize answers to the SAME
        // interview so the read-append-write below cannot drop a turn.
        // In-process ceiling documented on AsyncKeyedLock.
        using (await _lock.AcquireAsync($"mock:answer:{request.InterviewId}", cancellationToken))
        {
            var interview = await _db.MockInterviews
                .FirstOrDefaultAsync(m => m.Id == request.InterviewId, cancellationToken)
                ?? throw new NotFoundException("Interview not found.");

            if (interview.Status is MockInterviewStatus.Completed or MockInterviewStatus.Failed)
                throw new ConflictException("Interview has already ended.");

            var turns = string.IsNullOrEmpty(interview.TranscriptJson)
                ? new List<TranscriptTurn>()
                : JsonSerializer.Deserialize<List<TranscriptTurn>>(interview.TranscriptJson, TranscriptTurn.JsonOptions)
                  ?? new List<TranscriptTurn>();

            turns.Add(new TranscriptTurn(TranscriptTurn.CandidateRole, request.Message, null, DateTime.UtcNow));

            var followUp = await _generator.FollowUpAsync(turns, cancellationToken)
                ?? "Good. What's the time and space complexity of your approach, and which edge cases break it?";
            turns.Add(new TranscriptTurn(TranscriptTurn.InterviewerRole, followUp, null, DateTime.UtcNow));

            interview.TranscriptJson = JsonSerializer.Serialize(turns, TranscriptTurn.JsonOptions);
            await _db.SaveChangesAsync(cancellationToken);

            return ApiResponse<AnswerMockInterviewResultDto>.SuccessResult(
                new AnswerMockInterviewResultDto(followUp, turns.Count, InterviewOngoing: true));
        }
    }
}

using System.Text.Json.Serialization;
using VoxMentor.Domain.Enums;

namespace VoxMentor.Application.Features.Admin.GetTextbookJob;

/// <summary>Poll response for a textbook ingestion job.</summary>
public record TextbookJobDto(
    Guid JobId,
    string FileName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] TextbookJobStatus Status,
    int TotalChunks,
    int ProcessedChunks,
    string? Error,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

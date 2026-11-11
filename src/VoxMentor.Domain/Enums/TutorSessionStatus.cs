using System.Text.Json.Serialization;

namespace VoxMentor.Domain.Enums;

/// <summary>Lifecycle status of an AI tutor session.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TutorSessionStatus
{
    Pending = 0,
    Streaming = 1,
    Completed = 2,
    Failed = 3
}

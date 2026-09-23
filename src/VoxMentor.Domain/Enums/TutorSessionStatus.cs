namespace VoxMentor.Domain.Enums;

/// <summary>Lifecycle status of an AI tutor session.</summary>
public enum TutorSessionStatus
{
    Pending = 0,
    Streaming = 1,
    Completed = 2,
    Failed = 3
}

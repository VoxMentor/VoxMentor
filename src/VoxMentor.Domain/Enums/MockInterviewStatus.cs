namespace VoxMentor.Domain.Enums;

/// <summary>Lifecycle status of a mock interview, mirroring the tutor-session pattern.</summary>
public enum MockInterviewStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

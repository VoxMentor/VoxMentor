namespace VoxMentor.Domain.Enums;

/// <summary>Lifecycle status of a textbook ingestion job.</summary>
public enum TextbookJobStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

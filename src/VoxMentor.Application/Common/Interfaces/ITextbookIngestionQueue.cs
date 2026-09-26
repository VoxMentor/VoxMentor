namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Hands an ingestion job to the background runner. Implemented in Infrastructure
/// with Hangfire; Application stays free of job-framework references.
/// </summary>
public interface ITextbookIngestionQueue
{
    /// <summary>
    /// Staging directory (under the OS temp path) where uploads are written and
    /// IngestionJob confines its file access. Shared so both sides agree.
    /// </summary>
    const string TempDirectoryName = "vox-mentor-textbook";

    /// <summary>Queues chunk-and-embed processing for an already-persisted job.</summary>
    void Enqueue(Guid jobId, string filePath);
}

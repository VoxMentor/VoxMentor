using System.IO;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>Extracts plain text from uploaded .txt/.pdf files (bounded: 50 pages / 1M chars).</summary>
public interface ITextExtractor
{
    /// <exception cref="ValidationException">Unsupported type, invalid UTF-8, binary content, or over-limit file.</exception>
    Task<string> ExtractAsync(Stream stream, string extension, CancellationToken cancellationToken = default);
}

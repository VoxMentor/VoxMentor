using System;

namespace VoxMentor.Application.Common.Interfaces;

/// <summary>
/// Extracts plain text from a resume upload (PDF or plain text).
/// Implemented in Infrastructure (PdfPig for PDFs) so the Application
/// layer stays free of PDF dependencies.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Extracts text from the given stream. <paramref name="extension"/> must
    /// be ".pdf" or ".txt". Throws on corrupt/unreadable input; the caller
    /// converts failures to a 400.
    /// </summary>
    Task<string> ExtractAsync(Stream content, string extension, CancellationToken cancellationToken = default);
}

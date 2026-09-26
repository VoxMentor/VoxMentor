using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.UploadTextbook;

/// <summary>
/// Uploads a prep-content file (.pdf/.txt) for background chunking + embedding (#70).
/// </summary>
/// <param name="FileName">Original filename including extension.</param>
/// <param name="Content">Open stream of the uploaded file.</param>
/// <param name="Length">Declared file size in bytes (validated before reading).</param>
/// <param name="ConceptId">Optional concept every chunk is mapped to.</param>
public record UploadTextbookCommand(
    string FileName,
    Stream Content,
    long Length,
    Guid? ConceptId = null
) : IRequest<ApiResponse<UploadTextbookResultDto>>;

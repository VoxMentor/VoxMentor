namespace VoxMentor.Application.Features.Admin.UploadTextbook;

/// <summary>202 response for an accepted textbook upload.</summary>
public record UploadTextbookResultDto(Guid JobId, string Message);

using System.ComponentModel.DataAnnotations;

namespace VoxMentor.CodeExecService.Models;

public class ExecutionRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public int LanguageId { get; set; }

    public string? Stdin { get; set; }

    public string[]? ExpectedOutputs { get; set; }
}

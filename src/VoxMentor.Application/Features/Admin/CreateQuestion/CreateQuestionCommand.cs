using MediatR;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Application.Features.Admin.CreateQuestion;

/// <summary>
/// Creates a new practice question in the question bank. Requires Admin role.
/// </summary>
/// <param name="ConceptId">The DSA concept this question belongs to.</param>
/// <param name="Title">Short title (max 300 chars).</param>
/// <param name="Description">Full problem description.</param>
/// <param name="Difficulty">1-10 difficulty level.</param>
/// <param name="TestCases">All test cases (input/expected pairs stored as JSON strings).</param>
/// <param name="ExampleInputs">Public example inputs shown to students.</param>
/// <param name="ExampleOutputs">Public example outputs shown to students.</param>
/// <param name="StarterCode">Starter code templates per language.</param>
public record CreateQuestionCommand(
    Guid ConceptId,
    string Title,
    string Description,
    int Difficulty,
    string[] TestCases,
    string[]? ExampleInputs,
    string[]? ExampleOutputs,
    string[]? StarterCode
) : IRequest<ApiResponse<CreateQuestionResultDto>>;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoxMentor.CodeExecService.Models;
using VoxMentor.CodeExecService.Services;

namespace VoxMentor.CodeExecService.Controllers;

[ApiController]
[Route("api/v1/execute")]
public class ExecuteController : ControllerBase
{
    private readonly CodeExecutionService _executionService;

    public ExecuteController(CodeExecutionService executionService)
    {
        _executionService = executionService;
    }

    [HttpPost]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(ExecutionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExecuteCode(
        [FromBody] ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_executionService.IsLanguageSupported(request.LanguageId))
            return BadRequest(new { error = $"Language ID {request.LanguageId} is not supported. Supported: 51 (C#), 54 (C++), 62 (Java), 63 (JavaScript), 71 (Python)" });

        try
        {
            var result = await _executionService.ExecuteAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Code execution service unavailable", detail = ex.Message });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                new { error = "Code execution timed out" });
        }
    }
}

using System.Net;
using System.Text.Json;
using VoxMentor.Application.Common.Exceptions;
using VoxMentor.Application.Common.Models;

namespace VoxMentor.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex, _env.IsDevelopment());
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception, bool isDevelopment)
    {
        context.Response.ContentType = "application/json";

        var statusCode = HttpStatusCode.InternalServerError;
        var message = isDevelopment ? exception.Message : "An error occurred while processing your request.";
        IDictionary<string, string[]>? errors = null;

        switch (exception)
        {
            case ValidationException validationException:
                statusCode = HttpStatusCode.BadRequest;
                message = validationException.Message;
                errors = validationException.Errors;
                break;
            case ConflictException conflictException:
                statusCode = HttpStatusCode.Conflict;
                message = conflictException.Message;
                break;
            case UnauthorizedAccessException unauthorizedException:
                statusCode = HttpStatusCode.Unauthorized;
                message = unauthorizedException.Message;
                break;
            case NotFoundException notFoundException:
                statusCode = HttpStatusCode.NotFound;
                message = notFoundException.Message;
                break;
        }

        context.Response.StatusCode = (int)statusCode;

        if (isDevelopment && statusCode == HttpStatusCode.InternalServerError && exception.InnerException != null)
        {
            message += $" (Inner: {exception.InnerException.Message})";
        }

        var response = ApiResponse<object>.FailureResult(message, errors);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
    }
}

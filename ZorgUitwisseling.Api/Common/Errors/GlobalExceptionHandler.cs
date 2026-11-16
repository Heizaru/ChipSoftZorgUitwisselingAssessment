using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ZorgUitwisseling.Api.Common.Errors;

public class ExchangeValidationException(string message) : Exception(message);
public class ResourceNotFoundException(string message) : Exception(message);
public class ExchangeConflictException(string message) : Exception(message);

public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        (int status, string title, string detail) = exception switch
        {
            ExchangeValidationException => (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),
            ResourceNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            ExchangeConflictException => (StatusCodes.Status409Conflict, "Exchange conflict", exception.Message),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Invalid request", "The request body could not be read."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for request {TraceId}", httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
        return true;
    }
}

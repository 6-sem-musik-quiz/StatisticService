using Microsoft.AspNetCore.Diagnostics;
using StatisticService.Service.Weekly;

namespace StatisticService.Api.Middleware;

// Turns exceptions into ProblemDetails: a bad week is a 400, anything else a 500.
public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is InvalidWeekException)
        {
            logger.LogInformation("Rejected request for {Path}: {Message}", httpContext.Request.Path, exception.Message);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails =
                {
                    Title = "Invalid week.",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest
                }
            });
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError
            }
        });
    }
}

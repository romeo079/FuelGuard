using System.Net;

namespace FuelGuard.Api.Middleware;

public sealed class ProblemDetailsExceptionMiddleware(RequestDelegate next, ILogger<ProblemDetailsExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unhandled exception occurred while processing {Path}", context.Request.Path);

            var statusCode = context.Response.StatusCode == StatusCodes.Status200OK ? (int)HttpStatusCode.InternalServerError : context.Response.StatusCode;
            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = statusCode;

            await context.Response.WriteAsJsonAsync(new
            {
                title = "An unexpected error occurred.",
                status = statusCode,
                detail = ex.Message,
                instance = context.Request.Path
            });
        }
    }
}

using System.ComponentModel.DataAnnotations;

namespace Wbskt.Management.Api.Pipeline;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError("exception caught in middleware: {message}", ex.Message);
            context.Response.ContentType = "application/json";

            var (statusCode, message) = ex switch
            {
                UnauthorizedAccessException uex => (StatusCodes.Status401Unauthorized, uex.Message),
                ValidationException vex => (StatusCodes.Status400BadRequest, vex.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };

            context.Response.StatusCode = statusCode;

            var response = new
            {
                StatusCode = statusCode,
                Error = message,
                Details = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}

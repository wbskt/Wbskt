using System.Net;
using System.Text.Json;
using Wbskt.Auth.Api.Exceptions;

namespace Wbskt.Auth.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception has occurred.");

            var response = context.Response;
            response.ContentType = "application/json";

            response.StatusCode = ex switch
            {
                InvalidGrantException => (int)HttpStatusCode.BadRequest,
                _ => (int)HttpStatusCode.InternalServerError
            };

            var result = JsonSerializer.Serialize(new { message = ex.Message });
            await response.WriteAsync(result);
        }
    }
}
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.System;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Infrastructure.Middlewares;

public partial class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next, 
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IEventBus eventBus)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Faults are logged here, once, with the stack; services let them propagate rather than
            // logging them again and turning them into a Result.
            LogAnUnhandledExceptionOccurredMessage(LogLevel.Error, ex.Message, ex);
            await HandleExceptionAsync(context, ex, eventBus, _logger);
        }
    }

    // Safety net only. Expected failures travel as Result/Error and are given their status code by
    // ApiControllerBase; anything reaching here is either a deliberate throw from a non-controller
    // path or a genuine bug.
    private static async Task HandleExceptionAsync(HttpContext context, Exception exception, IEventBus eventBus, ILogger logger)
    {
        var (statusCode, code) = exception switch
        {
            // Providers throw this for "record not found" as well as access denial, deliberately, so
            // that a bad reference cannot be told apart from an inaccessible one. 403 rather than 401:
            // the caller is authenticated, they just cannot have this.
            SecurityException => (StatusCodes.Status403Forbidden, "AUTH_FORBIDDEN"),
            ValidationException or ArgumentException => (StatusCodes.Status400BadRequest, Problems.ValidationFailed),
            NotFoundException => (StatusCodes.Status404NotFound, "NOT_FOUND"),
            OptimisticConcurrencyException => (StatusCodes.Status409Conflict, "CONFLICT"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "AUTH_UNAUTHORIZED"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR")
        };

        var isServerFault = statusCode == StatusCodes.Status500InternalServerError;

        if (isServerFault)
        {
            try
            {
                await eventBus.PublishAsync(new SystemErrorEvent(
                    exception.GetType().Name,
                    exception.Message,
                    exception.StackTrace,
                    context.Request.Path,
                    context.TraceIdentifier
                ));
            }
            catch (Exception publishFailure)
            {
                // The caller still gets their 500 when the broker is down too.
                logger.LogWarning("Could not announce the unhandled exception: {Message}", publishFailure.Message);
            }
        }

        // The mapped types carry messages an author wrote for the caller, so those pass through.
        // An unmapped exception's message is arbitrary runtime text — SQL statements, file paths,
        // connection strings — and these hosts are publicly routed, so it is logged, not returned.
        // The trace ID is the handle for correlating the response with the logged detail.
        var message = isServerFault
            ? "An unexpected error occurred while processing the request."
            : exception.Message;

        await Problems.WriteAsync(context, statusCode, code, message);
    }

    [LoggerMessage("An unhandled exception occurred: {Message}")]
    partial void LogAnUnhandledExceptionOccurredMessage(LogLevel logLevel, string message, Exception exception);
}

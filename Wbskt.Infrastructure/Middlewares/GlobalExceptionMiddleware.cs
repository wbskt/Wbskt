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
        catch (Exception ex) when (context.RequestAborted.IsCancellationRequested && (ex is OperationCanceledException || ex is Microsoft.Data.SqlClient.SqlException))
        {
            // The caller went away (a browser tab closed, a fetch aborted): nothing failed on our side,
            // so it is not logged as a fault or announced. 499 is what proxies record for it.
            LogTheCallerCancelled(context.Request.Path);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodeClientClosedRequest;
            }
        }
        catch (Exception ex)
        {
            // Faults are logged here, once, with the stack; services let them propagate rather than
            // logging them again and turning them into a Result.
            LogAnUnhandledExceptionOccurredMessage(LogLevel.Error, ex.Message, ex);
            if (context.Response.HasStarted)
            {
                // Too late for a status or a body: the caller has part of a response already.
                _logger.LogWarning("The response had started, so the fault above could not be answered with an error.");
                return;
            }

            await HandleExceptionAsync(context, ex, eventBus, _logger);
        }
    }

    private const int StatusCodeClientClosedRequest = 499;

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
            // Not ArgumentException: one that escapes is a bug, a 500 like any other, and bad input
            // already travels as Error.Validation.
            ValidationException => (StatusCodes.Status400BadRequest, Problems.ValidationFailed),
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
                // The type, the path and the trace id only: the event log is readable by tenant admins,
                // and an exception's message and stack can quote SQL, addresses or file paths. The log
                // line above holds them, found by the trace id.
                await eventBus.PublishAsync(new SystemErrorEvent(
                    exception.GetType().Name,
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

    [LoggerMessage(LogLevel.Debug, "The caller cancelled the request to {Path}.")]
    partial void LogTheCallerCancelled(string path);

    [LoggerMessage("An unhandled exception occurred: {Message}")]
    partial void LogAnUnhandledExceptionOccurredMessage(LogLevel logLevel, string message, Exception exception);
}

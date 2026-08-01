using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.System;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Infrastructure.Middlewares;

public partial class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
            _logger.LogError("An unhandled exception occurred: {Message}", ex.Message);
            LogAnUnhandledExceptionOccurredMessage(LogLevel.Trace, ex.Message, ex);
            await HandleExceptionAsync(context, ex, eventBus);
        }
    }

    // Safety net only. Expected failures travel as Result/Error and are given their status code by
    // ApiControllerBase; anything reaching here is either a deliberate throw from a non-controller
    // path or a genuine bug.
    private static async Task HandleExceptionAsync(HttpContext context, Exception exception, IEventBus eventBus)
    {
        context.Response.ContentType = "application/json";

        var statusCode = exception switch
        {
            // Providers throw this for "record not found" as well as access denial, deliberately, so
            // that a bad reference cannot be told apart from an inaccessible one. 403 rather than 401:
            // the caller is authenticated, they just cannot have this.
            SecurityException => (int)HttpStatusCode.Forbidden,
            ValidationException => (int)HttpStatusCode.BadRequest,
            NotFoundException => (int)HttpStatusCode.NotFound,
            OptimisticConcurrencyException => (int)HttpStatusCode.Conflict,
            ArgumentException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };

        var isServerFault = statusCode == (int)HttpStatusCode.InternalServerError;

        if (isServerFault)
        {
            await eventBus.PublishAsync(new SystemErrorEvent(
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace,
                context.Request.Path,
                context.TraceIdentifier
            ));
        }

        context.Response.StatusCode = statusCode;

        // The mapped types carry messages an author wrote for the caller, so those pass through.
        // An unmapped exception's message is arbitrary runtime text — SQL statements, file paths,
        // connection strings — and these hosts are publicly routed, so it is logged, not returned.
        // The trace ID is the handle for correlating the response with the logged detail.
        var message = isServerFault
            ? "An unexpected error occurred while processing the request."
            : exception.Message;

        var response = new ErrorResponse(
            message,
            isServerFault ? nameof(InternalServerException) : exception.GetType().Name,
            context.TraceIdentifier
        );

        var result = JsonSerializer.Serialize(response, Options);

        await context.Response.WriteAsync(result);
    }

    [LoggerMessage("An unhandled exception occurred: {Message}")]
    partial void LogAnUnhandledExceptionOccurredMessage(LogLevel logLevel, string message, Exception exception);
}

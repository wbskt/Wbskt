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

public class GlobalExceptionMiddleware
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
            _logger.LogDebug(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex, eventBus);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception, IEventBus eventBus)
    {
        context.Response.ContentType = "application/json";
        
        var statusCode = exception switch
        {
            SecurityException => (int)HttpStatusCode.Unauthorized,
            ValidationException => (int)HttpStatusCode.BadRequest,
            NotFoundException => (int)HttpStatusCode.NotFound,
            ArgumentException => (int)HttpStatusCode.BadRequest,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };

        if (statusCode == (int)HttpStatusCode.InternalServerError)
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

        var response = new ErrorResponse(
            exception.Message,
            exception.GetType().Name,
            context.TraceIdentifier
        );

        var result = JsonSerializer.Serialize(response, Options);

        await context.Response.WriteAsync(result);
    }
}

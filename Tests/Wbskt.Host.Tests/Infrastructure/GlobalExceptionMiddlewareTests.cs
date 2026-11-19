using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.System;
using Wbskt.Infrastructure.Middlewares;

namespace Wbskt.Host.Tests.Infrastructure;

/// <summary>
/// The safety net: a fault escaping a request is a 500 announced once without its text, and a request
/// the caller abandoned is not a fault at all.
/// </summary>
public sealed class GlobalExceptionMiddlewareTests
{
    private readonly Mock<IEventBus> _bus = new();

    private async Task<HttpContext> RunAsync(Func<HttpContext, Task> next, bool aborted = false)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        if (aborted)
        {
            var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            context.RequestAborted = cancelled.Token;
        }

        await new GlobalExceptionMiddleware(c => next(c), NullLogger<GlobalExceptionMiddleware>.Instance).InvokeAsync(context, _bus.Object);
        return context;
    }

    private static string Body(HttpContext context) => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    // Each of these once had an arm of its own (400, 401, 403) that answered with the exception's text.
    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(Wbskt.Primitives.Exceptions.SecurityException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(KeyNotFoundException))]
    public async Task Any_exception_that_escapes_is_a_500_without_its_text(Type type)
    {
        var fault = (Exception)Activator.CreateInstance(type, "Value cannot be null. (Parameter 'connectionString')")!;

        var context = await RunAsync(_ => throw fault);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("INTERNAL_ERROR", Body(context));
        Assert.DoesNotContain("connectionString", Body(context));
        _bus.Verify(b => b.PublishAsync(It.Is<SystemErrorEvent>(e => e.ErrorType == type.Name), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_request_the_caller_abandoned_is_a_499_and_is_not_announced()
    {
        var context = await RunAsync(c => Task.FromCanceled(c.RequestAborted), aborted: true);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(string.Empty, Body(context));
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_cancellation_the_caller_did_not_ask_for_is_still_a_fault()
    {
        var context = await RunAsync(_ => throw new OperationCanceledException());

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        _bus.Verify(b => b.PublishAsync(It.IsAny<SystemErrorEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_fault_event_carries_the_type_path_and_trace_but_not_the_message()
    {
        SystemErrorEvent? published = null;
        _bus.Setup(b => b.PublishAsync(It.IsAny<SystemErrorEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SystemErrorEvent, CancellationToken>((e, _) => published = e)
            .Returns(Task.CompletedTask);

        await RunAsync(c =>
        {
            c.Request.Path = "/api/v1/things";
            c.TraceIdentifier = "trace-1";
            throw new InvalidOperationException("SELECT * FROM dbo.Secrets");
        });

        Assert.NotNull(published);
        Assert.Equal(nameof(InvalidOperationException), published!.ErrorType);
        Assert.Equal("/api/v1/things", published.RequestPath);
        Assert.Equal("trace-1", published.RequestId);
    }
}

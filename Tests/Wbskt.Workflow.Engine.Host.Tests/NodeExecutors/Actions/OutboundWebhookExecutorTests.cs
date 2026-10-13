using System.Net;
using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class OutboundWebhookExecutorTests
{
    [Fact]
    public async Task Returns_fail_non_retryable_when_address_guard_blocks_target()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateExecutor(handler, new StubAddressGuard(OutboundAddressDecision.Block("blocked (10.0.0.5)")));

        NodeExecutionResult result = await executor.ExecuteAsync(BuildContext("http://10.0.0.5/webhook", "POST"), CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("WEBHOOK_BLOCKED_TARGET", failure.ErrorCode);
        Assert.False(failure.Retryable);
        Assert.Null(handler.LastRequestUri); // guard runs before any request is sent
    }

    [Fact]
    public async Task Returns_fail_non_retryable_on_invalid_url()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateExecutor(handler);

        NodeExecutionResult result = await executor.ExecuteAsync(BuildContext("not-a-url", "POST"), CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("WEBHOOK_INVALID_URL", failure.ErrorCode);
        Assert.False(failure.Retryable);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task Posts_to_configured_url_with_body()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        });
        var executor = CreateExecutor(handler);
        JsonElement body = JsonSerializer.SerializeToElement(new { value = 1 });

        await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "POST", body), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://example.test/webhook", handler.LastRequestUri);
        Assert.Equal("{\"value\":1}", handler.LastBody);
    }

    [Fact]
    public async Task Returns_continue_on_2xx_response()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("done")
        });
        var executor = CreateExecutor(handler);

        NodeExecutionResult result = await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "POST"), CancellationToken.None);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", continuation.OutboundPort);
        Assert.Equal(200, continuation.LocalStatePatch["status"].GetInt32());
        Assert.Equal("done", continuation.LocalStatePatch["body"].GetString());
    }

    [Fact]
    public async Task Returns_fail_non_retryable_on_4xx_response()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request")
        });
        var executor = CreateExecutor(handler);

        NodeExecutionResult result = await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "POST"), CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("WEBHOOK_HTTP_ERROR", failure.ErrorCode);
        Assert.Equal("400: bad request", failure.Message);
        Assert.False(failure.Retryable);
        Assert.Null(failure.Cause);
    }

    [Fact]
    public async Task Returns_fail_retryable_on_5xx_response()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server error")
        });
        var executor = CreateExecutor(handler);

        NodeExecutionResult result = await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "POST"), CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("WEBHOOK_HTTP_ERROR", failure.ErrorCode);
        Assert.Equal("500: server error", failure.Message);
        Assert.True(failure.Retryable);
    }

    [Fact]
    public async Task Returns_fail_retryable_on_network_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        var executor = CreateExecutor(handler);

        NodeExecutionResult result = await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "POST"), CancellationToken.None);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(result);
        Assert.Equal("WEBHOOK_NETWORK_ERROR", failure.ErrorCode);
        Assert.Equal("network down", failure.Message);
        Assert.True(failure.Retryable);
        Assert.IsType<HttpRequestException>(failure.Cause);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/problem+json")]
    public async Task A_json_reply_is_also_handed_on_parsed(string mediaType)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"state":"on","level":42}""", System.Text.Encoding.UTF8, mediaType)
        });
        var executor = CreateExecutor(handler);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        JsonElement json = continuation.LocalStatePatch["json"];
        Assert.Equal("on", json.GetProperty("state").GetString());
        Assert.Equal(42, json.GetProperty("level").GetInt32());
        Assert.Equal("""{"state":"on","level":42}""", continuation.LocalStatePatch["body"].GetString());
    }

    [Theory]
    [InlineData("text/plain", """{"state":"on"}""")]
    [InlineData("application/json", "not json at all")]
    public async Task A_reply_that_is_not_json_has_no_parsed_output(string mediaType, string text)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, System.Text.Encoding.UTF8, mediaType)
        });
        var executor = CreateExecutor(handler);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.False(continuation.LocalStatePatch.ContainsKey("json"));
        Assert.Equal(text, continuation.LocalStatePatch["body"].GetString());
    }

    [Fact]
    public async Task A_reply_at_the_cap_is_read_whole()
    {
        string text = new('a', WebhookNotificationConfig.MaxResponseBytes);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        var executor = CreateExecutor(handler);

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.Equal(text.Length, continuation.LocalStatePatch["body"].GetString()!.Length);
    }

    [Fact]
    public async Task A_reply_over_the_cap_fails_without_a_retry()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('a', WebhookNotificationConfig.MaxResponseBytes + 1))
        });
        var executor = CreateExecutor(handler);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.Equal("WEBHOOK_RESPONSE_TOO_LARGE", failure.ErrorCode);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task An_oversized_error_reply_is_cut_in_the_failure_message()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(new string('e', WebhookNotificationConfig.MaxResponseBytes * 2))
        });
        var executor = CreateExecutor(handler);

        var failure = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.Equal("WEBHOOK_HTTP_ERROR", failure.ErrorCode);
        Assert.True(failure.Message.Length < WebhookNotificationConfig.MaxResponseBytes + 16);
        Assert.EndsWith("…", failure.Message);
    }

    [Fact]
    public async Task A_call_slower_than_the_step_timeout_fails_retryable()
    {
        var executor = CreateExecutor(new SlowHttpMessageHandler());
        var started = System.Diagnostics.Stopwatch.StartNew();

        var failure = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET", timeoutSeconds: 1), CancellationToken.None));

        Assert.Equal("WEBHOOK_TIMEOUT", failure.ErrorCode);
        Assert.True(failure.Retryable);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_cancelled_run_is_not_reported_as_a_timeout()
    {
        var executor = CreateExecutor(new SlowHttpMessageHandler());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), cts.Token));
    }

    [Fact]
    public async Task A_static_header_reaches_the_endpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = CreateExecutor(handler);
        var headers = new Dictionary<string, string> { ["X-Api-Version"] = "2024-01", ["Content-Type"] = "application/vnd.example+json" };

        await executor.ExecuteAsync(BuildContext("https://example.test/webhook", "POST", JsonSerializer.SerializeToElement(new { a = 1 }), headers: headers), CancellationToken.None);

        Assert.Equal("2024-01", handler.LastHeaders!["X-Api-Version"]);
        Assert.Equal("application/vnd.example+json", handler.LastHeaders["Content-Type"]);
    }

    private static WebhookNodeExecutor CreateExecutor(HttpMessageHandler handler, IOutboundAddressGuard? guard = null)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("workflow-webhook")).Returns(client);
        return new WebhookNodeExecutor(factory.Object, guard ?? new StubAddressGuard(OutboundAddressDecision.Allow()), NotificationExecutorTests.StubResolver.Failing("UNUSED"));
    }

    private sealed class StubAddressGuard(OutboundAddressDecision decision) : IOutboundAddressGuard
    {
        public ValueTask<OutboundAddressDecision> EvaluateAsync(Uri uri, CancellationToken ct) => ValueTask.FromResult(decision);
    }

    private static NodeContext BuildContext(string url, string method, JsonElement? body = null, int? timeoutSeconds = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        var branch = new BranchContext(
            42,
            1001,
            9,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            1,
            "node-1",
            1,
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement>(),
            "corr-42",
            new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc),
            9);
        var node = new WebhookNotificationNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "webhook", Ports = [], Config = new WebhookNotificationConfig { Url = url, Method = method, Body = body, TimeoutSeconds = timeoutSeconds, Headers = headers } };

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None, IdempotencyKey = "test-key",
        };
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public string? LastRequestUri { get; private set; }
        public string? LastBody { get; private set; }
        public Dictionary<string, string>? LastHeaders { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            LastHeaders = request.Headers.Concat(request.Content?.Headers.AsEnumerable() ?? [])
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            return responder(request);
        }
    }

    /// <summary>An endpoint that never answers within any timeout a test uses.</summary>
    private sealed class SlowHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

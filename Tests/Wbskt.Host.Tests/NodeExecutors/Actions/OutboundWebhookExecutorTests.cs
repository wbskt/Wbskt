using System.Net;
using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.NodeExecutors.Actions;

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
        // Outputs merge into the branch, so an earlier call's json would otherwise read as this one's.
        Assert.Contains("json", continuation.RemoveKeys!);
    }

    [Fact]
    public async Task A_json_reply_does_not_remove_its_own_parsed_output()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"a":1}""", System.Text.Encoding.UTF8, "application/json")
        });

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await CreateExecutor(handler).ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.True(continuation.LocalStatePatch.ContainsKey("json"));
        Assert.True(continuation.RemoveKeys is null || !continuation.RemoveKeys.Contains("json"));
    }

    public static TheoryData<string> BomEncodings => new() { "utf-8", "utf-16LE", "utf-16BE", "utf-32LE", "utf-32BE" };

    [Theory]
    [MemberData(nameof(BomEncodings))]
    public async Task A_json_reply_with_a_byte_order_mark_is_read_in_the_marks_encoding(string encodingName)
    {
        var encoding = encodingName switch
        {
            "utf-8" => new System.Text.UTF8Encoding(true),
            "utf-16LE" => new System.Text.UnicodeEncoding(false, true),
            "utf-16BE" => new System.Text.UnicodeEncoding(true, true),
            "utf-32LE" => new System.Text.UTF32Encoding(false, true),
            _ => (System.Text.Encoding)new System.Text.UTF32Encoding(true, true)
        };
        const string text = """{"state":"on"}""";
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
        var handler = new FakeHttpMessageHandler(_ =>
        {
            // No charset: the mark alone says how to read the body.
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var continuation = Assert.IsType<NodeExecutionResult.Continue>(await CreateExecutor(handler).ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.Equal(text, continuation.LocalStatePatch["body"].GetString());
        Assert.Equal("on", continuation.LocalStatePatch["json"].GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_connection_dropped_mid_body_fails_retryable_as_a_network_error()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(_ => throw new IOException("connection reset by peer")))
        });

        var failure = Assert.IsType<NodeExecutionResult.Fail>(await CreateExecutor(handler).ExecuteAsync(BuildContext("https://example.test/webhook", "GET"), CancellationToken.None));

        Assert.Equal("WEBHOOK_NETWORK_ERROR", failure.ErrorCode);
        Assert.Equal("connection reset by peer", failure.Message);
        Assert.True(failure.Retryable);
        Assert.IsType<IOException>(failure.Cause);
    }

    [Fact]
    public async Task A_body_that_stalls_past_the_timeout_is_a_timeout_even_when_the_stream_throws_io()
    {
        // The headers come back at once; the body never finishes, and its stream reports the
        // cancellation as a broken read rather than a cancellation.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FailingStream(async ct =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (OperationCanceledException ex)
                {
                    throw new IOException("the read was aborted", ex);
                }
            }))
        });

        var failure = Assert.IsType<NodeExecutionResult.Fail>(await CreateExecutor(handler).ExecuteAsync(BuildContext("https://example.test/webhook", "GET", timeoutSeconds: 1), CancellationToken.None));

        Assert.Equal("WEBHOOK_TIMEOUT", failure.ErrorCode);
        Assert.True(failure.Retryable);
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

    /// <summary>A body whose first read runs <paramref name="onRead"/>, which throws.</summary>
    internal sealed class FailingStream(Func<CancellationToken, Task> onRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await onRead(cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

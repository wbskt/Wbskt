using System.Net;
using System.Text.Json;
using Moq;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class OutboundWebhookExecutorTests
{
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

    private static WebhookNodeExecutor CreateExecutor(FakeHttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("workflow-webhook")).Returns(client);
        return new WebhookNodeExecutor(factory.Object);
    }

    private static NodeContext BuildContext(string url, string method, JsonElement? body = null)
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
        var node = new WebhookNotificationNode { NodeId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "webhook", Ports = [], Config = new WebhookNotificationConfig { Url = url, Method = method, Body = body } };

        return new NodeContext
        {
            Branch = branch,
            Node = node,
            Providers = Mock.Of<IProviderComposite>(),
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}

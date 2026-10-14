using System.Net;
using System.Text;
using System.Text.Json;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Host.Tests.Clients;

public sealed class WorkflowEngineClientTests
{
    // What the engine's controllers answer with: the shared contract, serialized as ASP.NET Core does.
    private static string Engine(InboundOutcome outcome, Guid? runRefId = null) =>
        JsonSerializer.Serialize(new InboundResponse(outcome, runRefId, []), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static HttpClient BuildClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://engine.test")
        };
    }

    [Fact]
    public async Task StartManualRunAsync_posts_to_correct_url()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(InboundOutcome.StartedRun, runRefId));
        var client = new WorkflowEngineClient(BuildClient(handler));
        var request = new StartRunRequest("manual-node", null);

        await client.StartManualRunAsync(workflowRefId, request, CancellationToken.None);

        Assert.Equal($"/api/inbound/manual/{workflowRefId}", handler.LastRequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Fact]
    public async Task StartManualRunAsync_maps_response_to_StartRunResponse()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(InboundOutcome.StartedRun, runRefId));
        var client = new WorkflowEngineClient(BuildClient(handler));
        var request = new StartRunRequest("node", null);

        StartRunResponse result = await client.StartManualRunAsync(workflowRefId, request, CancellationToken.None);

        Assert.Equal(runRefId, result.RunRefId);
    }

    [Theory]
    [InlineData(InboundOutcome.NoRegistration, StartRunOutcome.NoManualTrigger)]
    [InlineData(InboundOutcome.Queued, StartRunOutcome.Queued)]
    [InlineData(InboundOutcome.Dropped, StartRunOutcome.Dropped)]
    [InlineData(InboundOutcome.Idempotent, StartRunOutcome.Duplicate)]
    public async Task StartManualRunAsync_reports_non_start_outcomes_instead_of_throwing(InboundOutcome engineOutcome, StartRunOutcome expected)
    {
        // These are all normal results - a concurrency policy did its job, or a retry was
        // deduplicated. Throwing turned each of them into an HTTP 500.
        var workflowRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(engineOutcome));
        var client = new WorkflowEngineClient(BuildClient(handler));

        StartRunResponse response = await client.StartManualRunAsync(workflowRefId, new StartRunRequest("node", null), CancellationToken.None);

        Assert.Equal(expected, response.Outcome);
    }

    [Fact]
    public async Task StartManualRunAsync_reports_the_started_run()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(InboundOutcome.StartedRun, runRefId));
        var client = new WorkflowEngineClient(BuildClient(handler));

        StartRunResponse response = await client.StartManualRunAsync(workflowRefId, new StartRunRequest("node", null), CancellationToken.None);

        Assert.Equal(StartRunOutcome.Started, response.Outcome);
        Assert.Equal(runRefId, response.RunRefId);
    }

    [Fact]
    public async Task SignalAsync_posts_to_correct_url()
    {
        var runRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(InboundOutcome.ResumedBookmark));
        var client = new WorkflowEngineClient(BuildClient(handler));
        var request = new SignalRequest("wake", JsonSerializer.SerializeToElement(new { ready = true }));

        await client.SignalAsync(runRefId, "wake", request, CancellationToken.None);

        Assert.Equal($"/api/inbound/signal/{runRefId}/wake", handler.LastRequestUri?.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Fact]
    public async Task SignalAsync_maps_response_to_SignalResponse()
    {
        var runRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(InboundOutcome.ResumedBookmark));
        var client = new WorkflowEngineClient(BuildClient(handler));
        var request = new SignalRequest("wake", JsonSerializer.SerializeToElement(new { ready = true }));

        SignalResponse result = await client.SignalAsync(runRefId, "wake", request, CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("ResumedBookmark", result.Outcome);
    }

    [Fact]
    public async Task SignalAsync_escapes_the_signal_name_as_one_path_segment()
    {
        // The name is the workflow author's; unescaped, "a/b" would post to a different route.
        var runRefId = Guid.NewGuid();
        var handler = new StubHttpHandler(HttpStatusCode.OK, Engine(InboundOutcome.NoRegistration));
        var client = new WorkflowEngineClient(BuildClient(handler));

        await client.SignalAsync(runRefId, "a/b?c", new SignalRequest("a/b?c", JsonSerializer.SerializeToElement(new { })), CancellationToken.None);

        Assert.Equal($"/api/inbound/signal/{runRefId}/a%2Fb%3Fc", handler.LastRequestUri?.PathAndQuery);
    }

    private sealed class StubHttpHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}

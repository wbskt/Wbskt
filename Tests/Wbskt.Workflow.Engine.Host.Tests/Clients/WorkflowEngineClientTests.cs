using System.Net;
using System.Text;
using System.Text.Json;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Workflow.Engine.Host.Tests.Clients;

public sealed class WorkflowEngineClientTests
{
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
        var response = new { Outcome = "StartedRun", RunRefId = runRefId, RunId = 42 };
        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(response));
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
        var engineResponse = new { Outcome = "StartedRun", RunRefId = runRefId, RunId = (long)42 };
        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(engineResponse));
        var client = new WorkflowEngineClient(BuildClient(handler));
        var request = new StartRunRequest("node", null);

        StartRunResponse result = await client.StartManualRunAsync(workflowRefId, request, CancellationToken.None);

        Assert.Equal(runRefId, result.RunRefId);
        Assert.Equal(42, result.RunId);
    }

    [Theory]
    [InlineData("NoRegistration", StartRunOutcome.NoManualTrigger)]
    [InlineData("Queued", StartRunOutcome.Queued)]
    [InlineData("Dropped", StartRunOutcome.Dropped)]
    [InlineData("Idempotent", StartRunOutcome.Duplicate)]
    public async Task StartManualRunAsync_reports_non_start_outcomes_instead_of_throwing(string engineOutcome, StartRunOutcome expected)
    {
        // These are all normal results - a concurrency policy did its job, or a retry was
        // deduplicated. Throwing turned each of them into an HTTP 500.
        var workflowRefId = Guid.NewGuid();
        var engineResponse = new { Outcome = engineOutcome, RunRefId = (Guid?)null, RunId = (long?)null };
        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(engineResponse));
        var client = new WorkflowEngineClient(BuildClient(handler));

        StartRunResponse response = await client.StartManualRunAsync(workflowRefId, new StartRunRequest("node", null), CancellationToken.None);

        Assert.Equal(expected, response.Outcome);
    }

    [Fact]
    public async Task StartManualRunAsync_reports_the_started_run()
    {
        var workflowRefId = Guid.NewGuid();
        var runRefId = Guid.NewGuid();
        var engineResponse = new { Outcome = "StartedRun", RunRefId = (Guid?)runRefId, RunId = (long?)77 };
        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(engineResponse));
        var client = new WorkflowEngineClient(BuildClient(handler));

        StartRunResponse response = await client.StartManualRunAsync(workflowRefId, new StartRunRequest("node", null), CancellationToken.None);

        Assert.Equal(StartRunOutcome.Started, response.Outcome);
        Assert.Equal(runRefId, response.RunRefId);
        Assert.Equal(77, response.RunId);
    }

    [Fact]
    public async Task SignalAsync_posts_to_correct_url()
    {
        var runRefId = Guid.NewGuid();
        var engineResponse = new { Outcome = "ResumedBookmark", Matched = true };
        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(engineResponse));
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
        var engineResponse = new { Outcome = "ResumedBookmark", Matched = true };
        var handler = new StubHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(engineResponse));
        var client = new WorkflowEngineClient(BuildClient(handler));
        var request = new SignalRequest("wake", JsonSerializer.SerializeToElement(new { ready = true }));

        SignalResponse result = await client.SignalAsync(runRefId, "wake", request, CancellationToken.None);

        Assert.True(result.Matched);
        Assert.Equal("ResumedBookmark", result.Outcome);
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

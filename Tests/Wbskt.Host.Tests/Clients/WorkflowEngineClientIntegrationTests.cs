using System.Net;
using System.Text;
using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Workflow.Engine.Contracts;
using static Wbskt.Host.Tests.NodeExecutors.Actions.NotificationExecutorTests;

namespace Wbskt.Host.Tests.Clients;

/// <summary>How the engine's answers to seal and test reach the management host's callers.</summary>
public sealed class WorkflowEngineClientIntegrationTests
{
    private static readonly IntegrationSealRequest Seal = new(3, Guid.NewGuid(), "telegram", null, null, null, null);
    private static readonly IntegrationProbeRequest Probe = new(3, Guid.NewGuid(), "telegram", null, null, null, null, new IntegrationTestOptions("42", null));

    [Fact]
    public async Task Seal_posts_to_the_seal_route_and_returns_what_the_engine_sealed()
    {
        var sealedResponse = new IntegrationSealResponse(JsonSerializer.SerializeToElement(new { }), new SealedSecret([1, 2, 3], 1), "••••0123", true);
        var handler = new FakeHandler(_ => Answer(HttpStatusCode.OK, JsonSerializer.Serialize(sealedResponse, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var result = await Client(handler).SealIntegrationAsync(Seal, CancellationToken.None);

        Assert.Equal($"https://engine.test/{EngineIntegrationsApi.SealRoute}", handler.LastUri);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal([1, 2, 3], result.Value.Secret.Ciphertext);
        Assert.Equal("••••0123", result.Value.SecretHint);
        Assert.True(result.Value.SecretChanged);
    }

    [Fact]
    public async Task Probe_posts_to_the_test_route_with_its_options()
    {
        var handler = new FakeHandler(_ => Answer(HttpStatusCode.OK, "{\"ok\":false,\"code\":\"INTEGRATION_AUTH_FAILED\",\"message\":\"no\"}"));

        var result = await Client(handler).ProbeIntegrationAsync(Probe, CancellationToken.None);

        Assert.Equal($"https://engine.test/{EngineIntegrationsApi.TestRoute}", handler.LastUri);
        Assert.Contains("\"chatId\":\"42\"", handler.LastBody);
        // A failed test is an answer, not a failure.
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Ok);
        Assert.Equal("INTEGRATION_AUTH_FAILED", result.Value.Code);
    }

    [Fact]
    public async Task The_engines_validation_refusal_reaches_the_caller_as_is()
    {
        var handler = new FakeHandler(_ => Answer(HttpStatusCode.BadRequest, "{\"code\":\"INTEGRATION_SECRET_INVALID\",\"detail\":\"botToken is required.\"}"));

        var result = await Client(handler).SealIntegrationAsync(Seal, CancellationToken.None);

        Assert.Equal("INTEGRATION_SECRET_INVALID", result.Error.Code);
        Assert.Equal("botToken is required.", result.Error.Message);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task An_engine_without_a_master_key_is_unavailable_under_its_own_code()
    {
        var handler = new FakeHandler(_ => Answer(HttpStatusCode.ServiceUnavailable, "{\"code\":\"INTEGRATIONS_NOT_CONFIGURED\",\"detail\":\"no key\"}"));

        var result = await Client(handler).ProbeIntegrationAsync(Probe, CancellationToken.None);

        Assert.Equal("INTEGRATIONS_NOT_CONFIGURED", result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    [Theory]
    // A model-binding failure is the management host's request being wrong, not the caller's.
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"VALIDATION_FAILED\",\"detail\":\"bad\"}")]
    [InlineData(HttpStatusCode.BadRequest, "not json")]
    [InlineData(HttpStatusCode.Unauthorized, "{\"code\":\"UNAUTHORIZED\"}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"code\":\"SOMETHING_ELSE\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task Anything_else_reads_as_unavailable(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(_ => Answer(status, body));

        var result = await Client(handler).SealIntegrationAsync(Seal, CancellationToken.None);

        Assert.Equal("INTEGRATIONS_UNAVAILABLE", result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    [Fact]
    public async Task An_unreachable_engine_is_unavailable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("refused"));

        Assert.Equal("INTEGRATIONS_UNAVAILABLE", (await Client(handler).SealIntegrationAsync(Seal, CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task An_engine_that_times_out_is_unavailable()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout"));

        Assert.Equal("INTEGRATIONS_UNAVAILABLE", (await Client(handler).ProbeIntegrationAsync(Probe, CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task A_caller_that_gave_up_is_not_told_the_engine_is_unavailable()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new FakeHandler(_ => Answer(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).SealIntegrationAsync(Seal, cts.Token));
    }

    private static WorkflowEngineClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://engine.test/") });

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

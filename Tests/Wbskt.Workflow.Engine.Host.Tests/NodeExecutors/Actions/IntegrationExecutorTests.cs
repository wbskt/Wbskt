using System.Net;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.Runtime;
using static Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions.NotificationExecutorTests;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

/// <summary>The steps that send through an integration: Discord, ntfy, and Call a URL with an HTTP auth integration.</summary>
public sealed class IntegrationExecutorTests
{
    private const string WebhookUrl = "https://discord.com/api/webhooks/1/hook-secret-0000";
    private const string ApiToken = "api-token-abcdefgh";

    // -------------------------------------------------------------- discord

    [Fact]
    public async Task Discord_posts_through_the_integrations_webhook_and_marks_it_used()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var resolver = new StubResolver(Discord());
        var executor = new DiscordNodeExecutor(resolver, new DiscordChannel(new SingleClientFactory(handler)));

        var result = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(DiscordContext("Tank low", "Pump"), CancellationToken.None));

        Assert.Equal("default", result.OutboundPort);
        Assert.Equal(204, result.LocalStatePatch["status"].GetInt32());
        Assert.Equal(WebhookUrl, handler.LastUri);
        Assert.Contains("\"content\":\"Tank low\"", handler.LastBody);
        Assert.Equal(IntegrationType.Discord, resolver.Expected);
        Assert.Equal(1, resolver.MarkedUsed);
    }

    [Fact]
    public async Task Discord_passes_on_the_resolvers_failure_without_posting()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var executor = new DiscordNodeExecutor(StubResolver.Failing("INTEGRATION_NOT_FOUND"), new DiscordChannel(new SingleClientFactory(handler)));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(DiscordContext("hi"), CancellationToken.None));

        Assert.Equal("INTEGRATION_NOT_FOUND", fail.ErrorCode);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task Discord_failure_never_quotes_the_webhook_and_is_not_marked_used()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException($"could not connect to {WebhookUrl}"));
        var resolver = new StubResolver(Discord());
        var executor = new DiscordNodeExecutor(resolver, new DiscordChannel(new SingleClientFactory(handler)));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(DiscordContext("hi"), CancellationToken.None));

        Assert.Equal("DISCORD_NETWORK_ERROR", fail.ErrorCode);
        Assert.True(fail.Retryable);
        Assert.DoesNotContain("hook-secret", fail.Message);
        Assert.Equal(0, resolver.MarkedUsed);
    }

    // ----------------------------------------------------------------- ntfy

    [Fact]
    public async Task Ntfy_publishes_the_steps_message_to_the_integrations_topic()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var resolver = new StubResolver(Ntfy());
        var executor = new NtfyNodeExecutor(resolver, new NtfyChannel(new SingleClientFactory(handler), new AllowAllGuard()));

        var result = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(NtfyContext(), CancellationToken.None));

        Assert.Equal(200, result.LocalStatePatch["status"].GetInt32());
        Assert.Equal("https://ntfy.example.test/", handler.LastUri);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("pumps", body.RootElement.GetProperty("topic").GetString());
        Assert.Equal("Tank low", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(5, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal(IntegrationType.Ntfy, resolver.Expected);
        Assert.Equal(1, resolver.MarkedUsed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "NTFY_AUTH_FAILED", false)]
    [InlineData((HttpStatusCode)429, "NTFY_HTTP_ERROR", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "NTFY_HTTP_ERROR", true)]
    public async Task Ntfy_failures_are_named_for_the_step(HttpStatusCode status, string code, bool retryable)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));
        var resolver = new StubResolver(Ntfy());
        var executor = new NtfyNodeExecutor(resolver, new NtfyChannel(new SingleClientFactory(handler), new AllowAllGuard()));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(NtfyContext(), CancellationToken.None));

        Assert.Equal(code, fail.ErrorCode);
        Assert.Equal(retryable, fail.Retryable);
        Assert.Equal(0, resolver.MarkedUsed);
    }

    // ------------------------------------------------------ call a url (auth)

    [Fact]
    public async Task Webhook_appends_a_relative_url_to_the_base_url_and_adds_the_credential()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"state\":\"on\"}") });
        var resolver = new StubResolver(HttpAuth(HttpAuthSettings.Bearer));
        var guard = new RecordingGuard();
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), guard, resolver);

        var result = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(
            WebhookContext("api/services/light/turn_on", headers: new Dictionary<string, string> { ["X-Trace"] = "abc" }), CancellationToken.None));

        Assert.Equal("https://ha.example.test:8123/api/services/light/turn_on", handler.LastUri);
        Assert.Equal(handler.LastUri, guard.Evaluated?.ToString());
        Assert.Equal($"Bearer {ApiToken}", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal(["abc"], handler.LastRequest.Headers.GetValues("X-Trace"));
        Assert.Equal("{\"state\":\"on\"}", result.LocalStatePatch["body"].GetString());
        Assert.Equal(IntegrationType.HttpAuth, resolver.Expected);
        Assert.Equal(1, resolver.MarkedUsed);
    }

    [Fact]
    public async Task Webhook_takes_an_absolute_url_on_the_integrations_origin()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), new StubResolver(HttpAuth(HttpAuthSettings.Bearer)));

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(WebhookContext("https://ha.example.test:8123/api/states"), CancellationToken.None));

        Assert.Equal("https://ha.example.test:8123/api/states", handler.LastUri);
    }

    [Theory]
    [InlineData("https://evil.test/api/states")]
    [InlineData("http://ha.example.test:8123/api/states")]
    [InlineData("https://ha.example.test/api/states")]
    public async Task Webhook_never_sends_the_credential_off_the_integrations_origin(string url)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var resolver = new StubResolver(HttpAuth(HttpAuthSettings.Bearer));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), resolver);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(WebhookContext(url), CancellationToken.None));

        Assert.Equal("WEBHOOK_ORIGIN_MISMATCH", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Null(handler.LastUri);
        Assert.Equal(0, resolver.MarkedUsed);
    }

    [Theory]
    [InlineData(HttpAuthSettings.Bearer, "authorization")]
    [InlineData(HttpAuthSettings.Header, "x-api-key")]
    public async Task Webhook_refuses_a_header_the_credential_goes_in(string mode, string header)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), new StubResolver(HttpAuth(mode)));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(
            WebhookContext("api/states", headers: new Dictionary<string, string> { [header] = "mine" }), CancellationToken.None));

        Assert.Equal("WEBHOOK_HEADER_CONFLICT", fail.ErrorCode);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task Webhook_with_a_header_credential_may_still_set_authorization()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), new StubResolver(HttpAuth(HttpAuthSettings.Header)));

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(
            WebhookContext("api/states", headers: new Dictionary<string, string> { ["Authorization"] = "Token other" }), CancellationToken.None));

        Assert.Equal("Token other", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal([ApiToken], handler.LastRequest.Headers.GetValues("X-Api-Key"));
    }

    [Fact]
    public async Task Webhook_puts_a_content_type_header_on_the_body()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), new StubResolver(HttpAuth(HttpAuthSettings.Bearer)));

        await executor.ExecuteAsync(WebhookContext("api/states", JsonSerializer.SerializeToElement(new { a = 1 }), new Dictionary<string, string> { ["Content-Type"] = "application/merge-patch+json" }), CancellationToken.None);

        Assert.Equal("application/merge-patch+json", handler.LastRequest!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"a\":1}", handler.LastBody);
    }

    [Fact]
    public async Task Webhook_never_calls_a_blocked_integration_target()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new RecordingGuard(OutboundAddressDecision.Block("private")), new StubResolver(HttpAuth(HttpAuthSettings.Bearer)));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(WebhookContext("api/states"), CancellationToken.None));

        Assert.Equal("WEBHOOK_BLOCKED_TARGET", fail.ErrorCode);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task Webhook_scrubs_the_credential_out_of_an_error_body()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent($"upstream rejected Bearer {ApiToken}") });
        var resolver = new StubResolver(HttpAuth(HttpAuthSettings.Bearer));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), resolver);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(WebhookContext("api/states"), CancellationToken.None));

        Assert.Equal("WEBHOOK_HTTP_ERROR", fail.ErrorCode);
        Assert.True(fail.Retryable);
        Assert.Equal("502: upstream rejected Bearer ••••", fail.Message);
        // The credential reached the service, so it was used, whatever the service said.
        Assert.Equal(1, resolver.MarkedUsed);
    }

    [Fact]
    public async Task Webhook_passes_on_the_resolvers_failure_without_calling_out()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), StubResolver.Failing("INTEGRATION_WRONG_TYPE"));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(WebhookContext("api/states"), CancellationToken.None));

        Assert.Equal("INTEGRATION_WRONG_TYPE", fail.ErrorCode);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task Webhook_without_an_integration_does_not_resolve_one()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var resolver = new StubResolver(HttpAuth(HttpAuthSettings.Bearer));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), resolver);

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(WebhookContext("https://example.test/hook", withIntegration: false), CancellationToken.None));

        Assert.Equal(0, resolver.Calls);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    // ----------------------------------------------------------------- glue

    private static ResolvedIntegration Discord() =>
        new(5, Guid.NewGuid(), 9, IntegrationType.Discord, null, new DiscordSettings(), new IntegrationSecrets { WebhookUrl = WebhookUrl });

    private static ResolvedIntegration Ntfy() =>
        new(6, Guid.NewGuid(), 9, IntegrationType.Ntfy, null, new NtfySettings { ServerUrl = "https://ntfy.example.test" }, new IntegrationSecrets { Topic = "pumps" });

    private static ResolvedIntegration HttpAuth(string mode) =>
        new(7, Guid.NewGuid(), 9, IntegrationType.HttpAuth, null,
            new HttpAuthSettings { BaseUrl = "https://ha.example.test:8123", Mode = mode, HeaderName = mode == HttpAuthSettings.Header ? "X-Api-Key" : null },
            mode == HttpAuthSettings.Header ? new IntegrationSecrets { HeaderValue = ApiToken } : new IntegrationSecrets { Token = ApiToken });

    private static NodeContext DiscordContext(string message, string? username = null) => Context(new DiscordNotificationNode
    {
        NodeId = Guid.NewGuid(),
        Name = "discord",
        Ports = [],
        Config = new DiscordConfig { IntegrationRef = Guid.NewGuid(), Message = message, Username = username }
    });

    private static NodeContext NtfyContext() => Context(new NtfyNotificationNode
    {
        NodeId = Guid.NewGuid(),
        Name = "ntfy",
        Ports = [],
        Config = new NtfyConfig { IntegrationRef = Guid.NewGuid(), Message = "Tank low", Title = "Pump", Priority = 5, Tags = ["warning"] }
    });

    private static NodeContext WebhookContext(string url, JsonElement? body = null, Dictionary<string, string>? headers = null, bool withIntegration = true)
    {
        return Context(new WebhookNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "call",
            Ports = [],
            Config = new WebhookNotificationConfig
            {
                Url = url,
                Method = "POST",
                Body = body,
                Headers = headers,
                IntegrationRef = withIntegration ? Guid.NewGuid() : null
            }
        });
    }

    private sealed class RecordingGuard(OutboundAddressDecision? decision = null) : IOutboundAddressGuard
    {
        public Uri? Evaluated { get; private set; }

        public ValueTask<OutboundAddressDecision> EvaluateAsync(Uri uri, CancellationToken ct)
        {
            Evaluated = uri;
            return ValueTask.FromResult(decision ?? OutboundAddressDecision.Allow());
        }
    }
}

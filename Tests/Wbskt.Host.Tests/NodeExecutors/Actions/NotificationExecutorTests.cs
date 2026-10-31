using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.NodeExecutors.Actions;

public sealed class NotificationExecutorTests
{
    private const string Token = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    // ---------------------------------------------------------------- email

    [Fact]
    public async Task Email_fails_without_resolving_when_there_is_no_recipient()
    {
        var resolver = new StubResolver(Smtp());
        var executor = new EmailNodeExecutor(resolver, SmtpChannel());

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(EmailContext(to: " "), CancellationToken.None));

        Assert.Equal("EMAIL_RECIPIENT_MISSING", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Email_passes_on_the_resolvers_failure_without_sending()
    {
        // A step whose integration was deleted fails the same way on every attempt.
        var resolver = StubResolver.Failing("INTEGRATION_NOT_FOUND");
        var executor = new EmailNodeExecutor(resolver, SmtpChannel());

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(EmailContext(), CancellationToken.None));

        Assert.Equal("INTEGRATION_NOT_FOUND", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Equal(IntegrationType.Smtp, resolver.Expected);
    }

    [Fact]
    public async Task Email_treats_a_malformed_address_as_permanent()
    {
        var executor = new EmailNodeExecutor(new StubResolver(Smtp()), SmtpChannel());

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(EmailContext(to: "not an address"), CancellationToken.None));

        Assert.Equal("EMAIL_ADDRESS_INVALID", fail.ErrorCode);
        Assert.False(fail.Retryable);
    }

    // ------------------------------------------------------------- telegram

    [Fact]
    public async Task Telegram_posts_the_chat_id_and_message_as_the_integrations_bot()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        var resolver = new StubResolver(Telegram());
        var executor = new TelegramNodeExecutor(resolver, TelegramChannel(handler, "https://tg.test"));

        var result = await executor.ExecuteAsync(TelegramContext(), CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal($"https://tg.test/bot{Token}/sendMessage", handler.LastUri);
        Assert.Contains("\"chat_id\":\"-100123\"", handler.LastBody);
        Assert.Contains("\"text\":\"Tank low\"", handler.LastBody);
        Assert.Equal(IntegrationType.Telegram, resolver.Expected);
        Assert.Equal(1, resolver.MarkedUsed);
    }

    [Fact]
    public async Task Telegram_writes_the_sent_messages_ids()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":77,\"chat\":{\"id\":-1001234567890,\"type\":\"channel\"}}}")
        });
        var executor = new TelegramNodeExecutor(new StubResolver(Telegram()), TelegramChannel(handler));

        var result = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        var sent = result.LocalStatePatch["telegram"];
        Assert.Equal(77, sent.GetProperty("messageId").GetInt64());
        Assert.Equal(-1001234567890, sent.GetProperty("chatId").GetInt64());
    }

    [Fact]
    public async Task Telegram_leaves_out_the_ids_when_the_answer_has_none()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") });
        var executor = new TelegramNodeExecutor(new StubResolver(Telegram()), TelegramChannel(handler));

        var result = Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.False(result.LocalStatePatch.ContainsKey("telegram"));
    }

    [Fact]
    public async Task Telegram_fails_without_calling_out_when_the_integration_cannot_be_had()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new TelegramNodeExecutor(StubResolver.Failing("INTEGRATION_WRONG_TYPE"), TelegramChannel(handler));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.Equal("INTEGRATION_WRONG_TYPE", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Null(handler.LastUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "TELEGRAM_HTTP_ERROR", false)]
    [InlineData(HttpStatusCode.Unauthorized, "TELEGRAM_AUTH_FAILED", false)]
    // A 403 is the chat refusing the bot (blocked, kicked), not a bad token.
    [InlineData(HttpStatusCode.Forbidden, "TELEGRAM_CHAT_FORBIDDEN", false)]
    // Telegram rate-limits, and that is exactly what a retry is for.
    [InlineData((HttpStatusCode)429, "TELEGRAM_HTTP_ERROR", true)]
    [InlineData(HttpStatusCode.BadGateway, "TELEGRAM_HTTP_ERROR", true)]
    public async Task Telegram_retries_only_what_is_worth_retrying(HttpStatusCode status, string expectedCode, bool expectedRetryable)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });
        var resolver = new StubResolver(Telegram());
        var executor = new TelegramNodeExecutor(resolver, TelegramChannel(handler));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.Equal(expectedCode, fail.ErrorCode);
        Assert.Equal(expectedRetryable, fail.Retryable);
        Assert.Equal(0, resolver.MarkedUsed);
    }

    [Fact]
    public async Task Telegram_never_lets_the_bot_token_into_the_failure()
    {
        // The response can quote the request URL, and the request URL carries the bot token.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent($"{{\"description\":\"failed at https://api.telegram.org/bot{Token}/sendMessage\"}}")
        });
        var executor = new TelegramNodeExecutor(new StubResolver(Telegram()), TelegramChannel(handler));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.DoesNotContain(Token, fail.Message);
        Assert.Contains("failed at", fail.Message);
    }

    [Fact]
    public async Task Telegram_reports_a_network_failure_as_retryable_without_the_exception_text()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException($"connect to https://tg.test/bot{Token}/sendMessage failed"));
        var executor = new TelegramNodeExecutor(new StubResolver(Telegram()), TelegramChannel(handler));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.Equal("TELEGRAM_NETWORK_ERROR", fail.ErrorCode);
        Assert.True(fail.Retryable);
        Assert.DoesNotContain(Token, fail.Message);
    }

    // ----------------------------------------------------------------- glue

    internal static ResolvedIntegration Telegram() => new(1, Guid.NewGuid(), 9, IntegrationType.Telegram, null, new TelegramSettings(), new IntegrationSecrets { BotToken = Token });

    internal static ResolvedIntegration Smtp() => new(2, Guid.NewGuid(), 9, IntegrationType.Smtp, null,
        new SmtpSettings { Host = "smtp.example.test", Port = 587, Tls = SmtpSettings.StartTls, FromAddress = "alerts@example.test" },
        IntegrationSecrets.Empty);

    private static SmtpChannel SmtpChannel() => new(new AllowAllGuard(), Options.Create(new IntegrationOptions()));

    private static TelegramChannel TelegramChannel(HttpMessageHandler handler, string baseUrl = "https://api.telegram.org") =>
        new(new SingleClientFactory(handler), Options.Create(new IntegrationOptions { TelegramApiBaseUrl = baseUrl }));

    private static NodeContext EmailContext(string to = "ops@example.test")
    {
        var node = new EmailNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "mail",
            Ports = [],
            Config = new EmailConfig { To = to, Subject = "Tank low", Body = "Refill it.", IntegrationRef = Guid.NewGuid() }
        };
        return Context(node);
    }

    private static NodeContext TelegramContext()
    {
        var node = new TelegramNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "tg",
            Ports = [],
            Config = new TelegramConfig { ChatId = "-100123", Message = "Tank low", IntegrationRef = Guid.NewGuid() }
        };
        return Context(node);
    }

    internal static NodeContext Context(BaseNode node)
    {
        return new NodeContext
        {
            Branch = new BranchContext(1, 2, 3, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr", DateTime.UtcNow, 9),
            Node = node,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "key"
        };
    }

    /// <summary>Hands every step the one integration it was made with, or the one failure.</summary>
    internal sealed class StubResolver(ResolvedIntegration? integration, NodeExecutionResult.Fail? failure = null) : IIntegrationResolver
    {
        public int Calls { get; private set; }
        public int MarkedUsed { get; private set; }
        public IntegrationType? Expected { get; private set; }

        public static StubResolver Failing(string code) => new(null, new NodeExecutionResult.Fail(code, code, false, null));

        public Task<IntegrationResolution> ResolveAsync(BranchContext branch, Guid? refId, IntegrationType expected, CancellationToken ct)
        {
            Calls++;
            Expected = expected;
            return Task.FromResult(new IntegrationResolution(failure is null ? integration : null, failure));
        }

        public Task MarkUsedAsync(ResolvedIntegration resolved, CancellationToken ct)
        {
            MarkedUsed++;
            return Task.CompletedTask;
        }
    }

    internal sealed class AllowAllGuard : IOutboundAddressGuard
    {
        public ValueTask<OutboundAddressDecision> EvaluateAsync(Uri uri, CancellationToken ct) => ValueTask.FromResult(OutboundAddressDecision.Allow());
    }

    internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string? LastUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    internal sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}

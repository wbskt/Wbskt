using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Actions;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Actions;

public sealed class NotificationExecutorTests
{
    // ---------------------------------------------------------------- email

    [Fact]
    public async Task Email_sends_and_continues()
    {
        var sender = new RecordingEmailSender();
        var executor = new EmailNodeExecutor(sender);

        var result = await executor.ExecuteAsync(EmailContext(), CancellationToken.None);

        var cont = Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("default", cont.OutboundPort);
        Assert.Equal([("ops@example.test", "Tank low", "Refill it.")], sender.Sent);
    }

    [Fact]
    public async Task Email_fails_without_sending_when_no_relay_is_configured()
    {
        // Saying so beats failing somewhere inside the SMTP stack with a message about a null hostname,
        // and no number of retries configures a relay.
        var sender = new RecordingEmailSender { Configured = false };
        var executor = new EmailNodeExecutor(sender);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(EmailContext(), CancellationToken.None));

        Assert.Equal("EMAIL_NOT_CONFIGURED", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Email_treats_a_relay_error_as_retryable()
    {
        // SMTP is routinely transient - greylisting, a restarting relay, a reset connection.
        var sender = new RecordingEmailSender { Throw = new IOException("relay closed the connection") };
        var executor = new EmailNodeExecutor(sender);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(EmailContext(), CancellationToken.None));

        Assert.Equal("EMAIL_SEND_ERROR", fail.ErrorCode);
        Assert.True(fail.Retryable);
    }

    [Fact]
    public async Task Email_treats_a_malformed_address_as_permanent()
    {
        var sender = new RecordingEmailSender { Throw = new FormatException("not an address") };
        var executor = new EmailNodeExecutor(sender);

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(EmailContext(), CancellationToken.None));

        Assert.Equal("EMAIL_ADDRESS_INVALID", fail.ErrorCode);
        Assert.False(fail.Retryable);
    }

    // ------------------------------------------------------------- telegram

    [Fact]
    public async Task Telegram_posts_the_chat_id_and_message_to_the_bot_api()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        var executor = new TelegramNodeExecutor(new SingleClientFactory(handler), Options.Create(new TelegramOptions { BotToken = "tok", ApiBaseUrl = "https://tg.test" }));

        var result = await executor.ExecuteAsync(TelegramContext(), CancellationToken.None);

        Assert.IsType<NodeExecutionResult.Continue>(result);
        Assert.Equal("https://tg.test/bottok/sendMessage", handler.LastUri);
        Assert.Contains("\"chat_id\":\"-100123\"", handler.LastBody);
        Assert.Contains("\"text\":\"Tank low\"", handler.LastBody);
    }

    [Fact]
    public async Task Telegram_fails_without_calling_out_when_no_token_is_configured()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new TelegramNodeExecutor(new SingleClientFactory(handler));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.Equal("TELEGRAM_NOT_CONFIGURED", fail.ErrorCode);
        Assert.False(fail.Retryable);
        Assert.Null(handler.LastUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    // Telegram rate-limits, and that is exactly what a retry is for.
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    public async Task Telegram_retries_only_what_is_worth_retrying(HttpStatusCode status, bool expectedRetryable)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });
        var executor = new TelegramNodeExecutor(new SingleClientFactory(handler), Options.Create(new TelegramOptions { BotToken = "tok" }));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.Equal("TELEGRAM_HTTP_ERROR", fail.ErrorCode);
        Assert.Equal(expectedRetryable, fail.Retryable);
    }

    [Fact]
    public async Task Telegram_does_not_echo_the_api_response_body()
    {
        // The response can quote the request URL, and the request URL carries the bot token.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"description\":\"failed at https://api.telegram.org/botSUPERSECRET/sendMessage\"}")
        });
        var executor = new TelegramNodeExecutor(new SingleClientFactory(handler), Options.Create(new TelegramOptions { BotToken = "SUPERSECRET" }));

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(TelegramContext(), CancellationToken.None));

        Assert.DoesNotContain("SUPERSECRET", fail.Message);
    }

    // ----------------------------------------------------------------- glue

    private static NodeContext EmailContext()
    {
        var node = new EmailNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Name = "mail",
            Ports = [],
            Config = new EmailConfig { To = "ops@example.test", Subject = "Tank low", Body = "Refill it." }
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
            Config = new TelegramConfig { ChatId = "-100123", Message = "Tank low" }
        };
        return Context(node);
    }

    private static NodeContext Context(BaseNode node)
    {
        return new NodeContext
        {
            Branch = new BranchContext(1, 2, 3, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr", DateTime.UtcNow, 9),
            Node = node,
            Providers = new StubProviders(),
            Tick = 1,
            ParentResults = null,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "key"
        };
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public bool Configured { get; init; } = true;
        public Exception? Throw { get; init; }
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public bool IsConfigured => Configured;

        public Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public string? LastUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubProviders : IProviderComposite
    {
        public IWorkflowDefinitionProvider WorkflowDefinition => throw new NotSupportedException();
        public ITriggerRegistrationProvider TriggerRegistration => throw new NotSupportedException();
        public IBookmarkProvider Bookmark => throw new NotSupportedException();
        public ISharedVariableProvider SharedVariable => throw new NotSupportedException();
        public IIdempotencyKeyProvider IdempotencyKey => throw new NotSupportedException();
        public IPendingTriggerEventProvider PendingTriggerEvent => throw new NotSupportedException();
        public IScheduledFireProvider ScheduledFire => throw new NotSupportedException();
    }
}

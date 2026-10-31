using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.NodeExecutors.Actions;
using Wbskt.Workflow.Runtime;
using static Wbskt.Host.Tests.NodeExecutors.Actions.NotificationExecutorTests;

namespace Wbskt.Host.Tests.NodeExecutors.Actions;

public sealed class ActionTemplatesTests
{
    private static readonly Guid NodeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BranchRefId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ------------------------------------------------------------- text

    [Fact]
    public async Task Text_fills_in_branch_state_trigger_payload_and_shared_variables()
    {
        var templates = TestTemplates.Create(Shared(("site", "\"greenhouse\"")));
        var ctx = Context();

        ToastConfig filled = await templates.FillAsync(ctx, Toast("Frost: {{reading.temp}}°C in the {{ $shared.site }} ({{device}})"), CancellationToken.None);

        Assert.Equal("Frost: 1.4°C in the greenhouse (probe-7)", filled.Message);
        Assert.Empty(ctx.Warnings);
    }

    [Fact]
    public async Task A_config_without_placeholders_is_returned_as_it_is()
    {
        ToastConfig config = Toast("Tank low");

        Assert.Same(config, await TestTemplates.Create().FillAsync(Context(), config, CancellationToken.None));
    }

    [Fact]
    public async Task Filling_in_leaves_the_definitions_config_as_written()
    {
        ToastConfig config = Toast("{{device}}");

        await TestTemplates.Create().FillAsync(Context(), config, CancellationToken.None);

        Assert.Equal("{{device}}", config.Message);
    }

    [Fact]
    public async Task A_missing_value_is_empty_text_and_a_warning_naming_the_field_and_placeholder()
    {
        var ctx = Context();

        ToastConfig filled = await TestTemplates.Create(Shared()).FillAsync(ctx, Toast("[{{nope.temp}}][{{$shared.token}}][{{device}}]"), CancellationToken.None);

        Assert.Equal("[][][probe-7]", filled.Message);
        NodeWarning warning = Assert.Single(ctx.Warnings);
        Assert.Equal(HistoryEventKind.TemplateValueMissing, warning.EventKind);
        Assert.Equal(HistoryEventKind.SeverityWarn, HistoryEventKind.SeverityFor(warning.EventKind));
        Assert.Equal("""{"field":"message","placeholders":["nope.temp","$shared.token"]}""", JsonSerializer.Serialize(warning.Payload));
    }

    [Fact]
    public async Task A_long_number_is_filled_in_as_written()
    {
        var ctx = Context() with
        {
            Branch = Context().Branch with
            {
                TriggerPayload = new Dictionary<string, JsonElement> { ["thread"] = JsonDocument.Parse("1200000000000000002").RootElement.Clone() }
            }
        };

        DiscordConfig filled = await TestTemplates.Create().FillAsync(ctx, new DiscordConfig { Message = "m", ThreadId = "{{thread}}" }, CancellationToken.None);

        Assert.Equal("1200000000000000002", filled.ThreadId);
    }

    [Fact]
    public async Task A_single_line_field_turns_line_breaks_a_value_brings_in_into_spaces()
    {
        var config = new EmailConfig { To = "ops@example.test", Subject = "Alert: {{note}}", Body = "{{note}}" };

        EmailConfig filled = await TestTemplates.Create().FillAsync(Context(), config, CancellationToken.None);

        Assert.Equal("Alert: one two  three", filled.Subject);
        Assert.Equal("one\r\ntwo\n\nthree", filled.Body);
    }

    [Fact]
    public async Task A_malformed_placeholder_fails_the_step()
    {
        var ex = await Assert.ThrowsAsync<ExpressionEvaluationException>(() =>
            TestTemplates.Create().FillAsync(Context(), Toast("temp {{reading.temp"), CancellationToken.None));

        Assert.Contains("unclosed", ex.Message);
    }

    [Fact]
    public async Task Nested_records_lists_and_header_values_are_filled_in_and_named_by_their_path()
    {
        var ctx = Context();
        var discord = new DiscordConfig
        {
            Message = "{{device}}",
            Embeds = [new DiscordEmbed { Title = "t", Fields = [new DiscordEmbedField { Name = "temp", Value = "{{reading.temp}} {{gone}}" }] }]
        };
        var webhook = new WebhookNotificationConfig { Url = "https://x.test/{{device}}", Method = "POST", Headers = new Dictionary<string, string> { ["X-Who"] = "{{gone}}" } };
        var ntfy = new NtfyConfig { Message = "m", Tags = ["{{device}}", "fixed"] };
        var templates = TestTemplates.Create();

        DiscordConfig filledDiscord = await templates.FillAsync(ctx, discord, CancellationToken.None);
        WebhookNotificationConfig filledWebhook = await templates.FillAsync(ctx, webhook, CancellationToken.None);
        NtfyConfig filledNtfy = await templates.FillAsync(ctx, ntfy, CancellationToken.None);

        Assert.Equal("1.4 ", filledDiscord.Embeds![0].Fields![0].Value);
        Assert.Equal("https://x.test/probe-7", filledWebhook.Url);
        Assert.Equal(["probe-7", "fixed"], filledNtfy.Tags);
        Assert.Equal(
            ["embeds[0].fields[0].value", "headers.X-Who"],
            ctx.Warnings.Select(w => JsonSerializer.SerializeToElement(w.Payload).GetProperty("field").GetString()));
    }

    // ------------------------------------------------------------- json

    [Fact]
    public async Task Json_keeps_the_type_of_a_leaf_that_is_one_placeholder_and_fills_in_the_rest()
    {
        var ctx = Context();
        JsonElement payload = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["on"] = "{{heaterOn}}",
            ["temp"] = "{{reading.temp}}",
            ["reading"] = "{{reading}}",
            ["label"] = "{{device}} at {{reading.temp}}",
            ["{{device}}"] = new object[] { "{{reading.temp}}", 3, true },
            ["fixed"] = "as written"
        });

        SendClientMessageConfig filled = await TestTemplates.Create().FillAsync(ctx, Command(payload), CancellationToken.None);

        Assert.Equal(
            """{"on":true,"temp":1.4,"reading":{"temp":1.4},"label":"probe-7 at 1.4","{{device}}":[1.4,3,true],"fixed":"as written"}""",
            filled.Payload!.Value.GetRawText());
        Assert.Empty(ctx.Warnings);
    }

    [Fact]
    public async Task Json_turns_a_missing_leaf_into_empty_text_and_warns_at_its_path()
    {
        var ctx = Context();
        JsonElement payload = JsonSerializer.SerializeToElement(new { items = new object[] { new { on = "{{nope}}" } } });

        SendClientMessageConfig filled = await TestTemplates.Create().FillAsync(ctx, Command(payload), CancellationToken.None);

        Assert.Equal("""{"items":[{"on":""}]}""", filled.Payload!.Value.GetRawText());
        NodeWarning warning = Assert.Single(ctx.Warnings);
        Assert.Equal("""{"field":"payload.items[0].on","placeholders":["nope"]}""", JsonSerializer.Serialize(warning.Payload));
    }

    // --------------------------------------------------- warnings reach history

    [Fact]
    public async Task The_retry_executor_hands_on_only_the_deciding_attempts_warnings()
    {
        var node = new ToastNotificationNode
        {
            NodeId = NodeId,
            Name = "toast",
            Ports = [],
            Config = Toast("{{gone}}"),
            Retry = new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 3, JitterPct = 0, RetryOn = [] }
        };
        var publisher = new Mock<IToastPublisher>();
        publisher.SetupSequence(p => p.PublishToastAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("bus"))
            .Returns(Task.CompletedTask);

        NodeExecutionResult result = await RetryExecutor.RunWithRetryAsync(
            node,
            Context(node).Branch,
            new ToastNodeExecutor(publisher.Object, TestTemplates.Create()),
            new NodeExecutionServices(new InMemoryIdempotencyKeyProvider()),
            TimeProvider.System,
            CancellationToken.None,
            new UnlimitedRunCountersProvider(),
            new DefaultCreditCostCalculator());

        Assert.IsType<NodeExecutionResult.Continue>(result);
        NodeWarning warning = Assert.Single(result.Warnings);
        Assert.Equal("""{"field":"message","placeholders":["gone"]}""", JsonSerializer.Serialize(warning.Payload));
    }

    // -------------------------------------------------------- executors

    [Fact]
    public async Task Telegram_sends_the_filled_in_message()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        var channel = new TelegramChannel(new SingleClientFactory(handler), Options.Create(new IntegrationOptions()));
        var executor = new TelegramNodeExecutor(new StubResolver(Telegram()), channel, TestTemplates.Create());
        var node = new TelegramNotificationNode
        {
            NodeId = NodeId,
            Name = "tg",
            Ports = [],
            Config = new TelegramConfig { ChatId = "-100123", Message = "Frost: {{reading.temp}} C", IntegrationRef = Guid.NewGuid() }
        };

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(node), CancellationToken.None));

        Assert.Contains("\"text\":\"Frost: 1.4 C\"", handler.LastBody);
    }

    [Fact]
    public async Task Webhook_fills_in_the_url_headers_and_body_before_checking_the_target()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var executor = new WebhookNodeExecutor(new SingleClientFactory(handler), new AllowAllGuard(), StubResolver.Failing("UNUSED"), TestTemplates.Create());
        var node = new WebhookNotificationNode
        {
            NodeId = NodeId,
            Name = "call",
            Ports = [],
            Config = new WebhookNotificationConfig
            {
                Url = "https://hooks.example.test/{{device}}",
                Method = "POST",
                Headers = new Dictionary<string, string> { ["X-Note"] = "{{note}}" },
                Body = JsonSerializer.SerializeToElement(new { on = "{{heaterOn}}" })
            }
        };

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(node), CancellationToken.None));

        Assert.Equal("https://hooks.example.test/probe-7", handler.LastUri);
        Assert.Equal(["one two  three"], handler.LastRequest!.Headers.GetValues("X-Note"));
        Assert.Equal("""{"on":true}""", handler.LastBody);
    }

    [Fact]
    public async Task Webhook_quotes_the_url_as_written_when_the_filled_in_one_is_refused()
    {
        var executor = new WebhookNodeExecutor(new SingleClientFactory(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))), new AllowAllGuard(), StubResolver.Failing("UNUSED"), TestTemplates.Create());
        var node = new WebhookNotificationNode
        {
            NodeId = NodeId,
            Name = "call",
            Ports = [],
            Config = new WebhookNotificationConfig { Url = "{{device}}", Method = "POST" }
        };

        var fail = Assert.IsType<NodeExecutionResult.Fail>(await executor.ExecuteAsync(Context(node), CancellationToken.None));

        Assert.Equal("WEBHOOK_INVALID_URL", fail.ErrorCode);
        Assert.Contains("'{{device}}'", fail.Message);
        Assert.DoesNotContain("probe-7", fail.Message);
    }

    [Fact]
    public async Task Send_to_device_sends_the_filled_in_payload()
    {
        var publisher = new Mock<IDeviceCommandPublisher>();
        var executor = new CommandNodeExecutor(publisher.Object, Mock.Of<IBookmarkResumer>(), TestTemplates.Create());
        var node = new SendClientMessageNode
        {
            NodeId = NodeId,
            Name = "command",
            Ports = [],
            Config = new SendClientMessageConfig
            {
                ClientRef = Guid.NewGuid().ToString(),
                Type = "SetHeater",
                Payload = JsonSerializer.SerializeToElement(new { on = "{{heaterOn}}", reason = "{{device}} read {{reading.temp}}" })
            }
        };

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(node), CancellationToken.None));

        publisher.Verify(p => p.PublishCommandAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), "SetHeater",
            """{"on":true,"reason":"probe-7 read 1.4"}""",
            It.IsAny<TimeSpan>(), It.IsAny<CommandSender?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Toast_shows_the_filled_in_title_and_message()
    {
        var publisher = new Mock<IToastPublisher>();
        var executor = new ToastNodeExecutor(publisher.Object, TestTemplates.Create());
        var node = new ToastNotificationNode
        {
            NodeId = NodeId,
            Name = "toast",
            Ports = [],
            Config = new ToastConfig { Title = "{{device}}", Message = "{{reading.temp}}°C" }
        };

        Assert.IsType<NodeExecutionResult.Continue>(await executor.ExecuteAsync(Context(node), CancellationToken.None));

        publisher.Verify(p => p.PublishToastAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<int>(), "probe-7", "1.4°C", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------- glue

    private static ToastConfig Toast(string message) => new() { Title = "t", Message = message };

    private static SendClientMessageConfig Command(JsonElement payload) => new() { ClientRef = Guid.NewGuid().ToString(), Type = "SetHeater", Payload = payload };

    private static NodeContext Context(BaseNode? node = null)
    {
        node ??= new ToastNotificationNode { NodeId = NodeId, Name = "toast", Ports = [], Config = new ToastConfig { Title = "t", Message = "m" } };
        var local = new Dictionary<string, JsonElement>
        {
            ["reading"] = JsonSerializer.SerializeToElement(new { temp = 1.4 }),
            ["heaterOn"] = JsonSerializer.SerializeToElement(true),
            ["note"] = JsonSerializer.SerializeToElement("one\r\ntwo\n\nthree")
        };
        var trigger = new Dictionary<string, JsonElement> { ["device"] = JsonSerializer.SerializeToElement("probe-7") };

        return new NodeContext
        {
            Branch = new BranchContext(42, 7, 3, Guid.NewGuid(), 1, node.NodeId.ToString(), 1, local, trigger, "corr", DateTime.UtcNow, 9)
            {
                BranchRefId = BranchRefId
            },
            Node = node,
            CancellationToken = CancellationToken.None,
            IdempotencyKey = "key"
        };
    }

    private static ISharedVariableProvider Shared(params (string Name, string ValueJson)[] values)
    {
        var provider = new Mock<ISharedVariableProvider>();
        provider
            .Setup(p => p.GetByWorkflowRefIdNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid workflowRefId, string name, CancellationToken _) =>
            {
                foreach (var (n, valueJson) in values)
                {
                    if (n == name)
                    {
                        return Task.FromResult(new SharedVariableRow
                        {
                            Id = 1,
                            WorkflowRefId = workflowRefId,
                            VarName = name,
                            VarType = "Json",
                            ValueJson = valueJson,
                            UpdatedAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                throw new KeyNotFoundException(name);
            });
        return provider.Object;
    }
}

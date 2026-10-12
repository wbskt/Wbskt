using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

/// <summary>What the validator can tell about integration steps from the definition alone.</summary>
public sealed class IntegrationStepValidationTests
{
    private static readonly WorkflowValidator Validator = new();

    public static TheoryData<BaseNode, string> StepsWithoutIntegration => new()
    {
        { Telegram(null), "telegram" },
        { Email(null), "smtp" },
        { Discord(null), "discord" },
        { Ntfy(null), "ntfy" },
        { Telegram(Guid.Empty), "telegram" }
    };

    [Theory]
    [MemberData(nameof(StepsWithoutIntegration))]
    public void A_step_that_sends_through_an_integration_must_name_one(BaseNode node, string typeName)
    {
        var issue = Assert.Single(IssuesFor(node), i => i.Code == "INTEGRATION_REF_MISSING");

        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Contains(typeName, issue.Message);
    }

    [Fact]
    public void Call_a_url_does_not_need_an_integration()
    {
        Assert.Empty(IssuesFor(Webhook("https://example.test/hook", null)));
    }

    [Fact]
    public void Steps_that_name_their_integration_pass()
    {
        Assert.Empty(IssuesFor(Telegram(Guid.NewGuid())));
        Assert.Empty(IssuesFor(Email(Guid.NewGuid())));
        Assert.Empty(IssuesFor(Discord(Guid.NewGuid())));
        Assert.Empty(IssuesFor(Ntfy(Guid.NewGuid())));
        Assert.Empty(IssuesFor(Webhook("api/states", Guid.NewGuid())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Telegram_needs_a_chat(string chatId)
    {
        var node = Telegram(Guid.NewGuid()) with { Config = new TelegramConfig { IntegrationRef = Guid.NewGuid(), ChatId = chatId, Message = "m" } };

        Assert.Contains(IssuesFor(node), i => i.Code == "TELEGRAM_CHAT_MISSING");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("x", true)]
    public void Discord_needs_a_message(string message, bool valid)
    {
        var node = Discord(Guid.NewGuid()) with { Config = new DiscordConfig { IntegrationRef = Guid.NewGuid(), Message = message } };

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "DISCORD_MESSAGE_INVALID"));
    }

    [Fact]
    public void Discord_message_is_at_most_2000_characters()
    {
        var ok = Discord(Guid.NewGuid()) with { Config = new DiscordConfig { IntegrationRef = Guid.NewGuid(), Message = new string('a', 2000) } };
        var tooLong = ok with { Config = ok.Config with { Message = new string('a', 2001) } };

        Assert.DoesNotContain(IssuesFor(ok), i => i.Code == "DISCORD_MESSAGE_INVALID");
        Assert.Contains(IssuesFor(tooLong), i => i.Code == "DISCORD_MESSAGE_INVALID");
    }

    [Fact]
    public void Ntfy_needs_a_message()
    {
        var node = Ntfy(Guid.NewGuid()) with { Config = new NtfyConfig { IntegrationRef = Guid.NewGuid(), Message = " " } };

        Assert.Contains(IssuesFor(node), i => i.Code == "NTFY_MESSAGE_MISSING");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Ntfy_priority_is_1_to_5(int priority, bool valid)
    {
        var node = Ntfy(Guid.NewGuid()) with { Config = new NtfyConfig { IntegrationRef = Guid.NewGuid(), Message = "m", Priority = priority } };

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "NTFY_PRIORITY_INVALID"));
    }

    [Theory]
    [InlineData("api/states")]
    [InlineData("/api/states")]
    [InlineData("example.test/hook")]
    public void A_relative_url_needs_an_integration_to_be_relative_to(string url)
    {
        Assert.Contains(IssuesFor(Webhook(url, null)), i => i.Code == "WEBHOOK_URL_RELATIVE");
        Assert.DoesNotContain(IssuesFor(Webhook(url, Guid.NewGuid())), i => i.Code == "WEBHOOK_URL_RELATIVE");
    }

    [Fact]
    public void A_step_may_set_up_to_20_headers()
    {
        var twenty = Enumerable.Range(1, 20).ToDictionary(i => $"X-H{i}", _ => "v");
        var twentyOne = Enumerable.Range(1, 21).ToDictionary(i => $"X-H{i}", _ => "v");

        Assert.DoesNotContain(IssuesFor(Webhook("https://example.test", null, twenty)), i => i.Code == "WEBHOOK_HEADERS_TOO_MANY");
        Assert.Contains(IssuesFor(Webhook("https://example.test", null, twentyOne)), i => i.Code == "WEBHOOK_HEADERS_TOO_MANY");
    }

    [Theory]
    [InlineData("X Trace", "v")]
    [InlineData("X-Trace:", "v")]
    [InlineData("", "v")]
    [InlineData("X-Trace", "a\r\nX-Injected: 1")]
    [InlineData("X-Trace", "a\0b")]
    [InlineData("X-Tracé", "v")]
    public void A_header_must_be_a_valid_name_and_value(string name, string value)
    {
        var issues = IssuesFor(Webhook("https://example.test", null, new Dictionary<string, string> { [name] = value }));

        Assert.Contains(issues, i => i.Code == "WEBHOOK_HEADER_INVALID");
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Cookie")]
    [InlineData("Host")]
    [InlineData("Content-Length")]
    public void A_step_may_not_set_a_credential_or_a_header_the_request_owns(string name)
    {
        var issues = IssuesFor(Webhook("https://example.test", null, new Dictionary<string, string> { [name] = "v" }));

        Assert.Contains(issues, i => i.Code == "WEBHOOK_HEADER_FORBIDDEN");
    }

    [Fact]
    public void A_step_may_set_ordinary_headers_and_a_content_type()
    {
        var headers = new Dictionary<string, string> { ["X-Trace"] = "abc", ["Content-Type"] = "application/json", ["Accept"] = "*/*" };

        Assert.Empty(IssuesFor(Webhook("https://example.test", null, headers)));
    }

    [Fact]
    public void Every_integration_step_kind_maps_to_its_type()
    {
        Assert.Equal(IntegrationType.Telegram, IntegrationTypes.ForStepKind(NodeKind.ActionTelegram));
        Assert.Equal(IntegrationType.Smtp, IntegrationTypes.ForStepKind(NodeKind.ActionEmail));
        Assert.Equal(IntegrationType.Discord, IntegrationTypes.ForStepKind(NodeKind.ActionDiscord));
        Assert.Equal(IntegrationType.Ntfy, IntegrationTypes.ForStepKind(NodeKind.ActionNtfy));
        Assert.Equal(IntegrationType.HttpAuth, IntegrationTypes.ForStepKind(NodeKind.ActionWebhook));
        Assert.Null(IntegrationTypes.ForStepKind(NodeKind.ActionToast));
        Assert.True(IntegrationTypes.IsRequiredFor(NodeKind.ActionNtfy));
        Assert.False(IntegrationTypes.IsRequiredFor(NodeKind.ActionWebhook));
        Assert.False(IntegrationTypes.IsRequiredFor(NodeKind.ActionToast));
    }

    [Fact]
    public void Type_and_preset_names_are_case_sensitive()
    {
        Assert.True(IntegrationTypes.TryParse("httpAuth", out var type));
        Assert.Equal(IntegrationType.HttpAuth, type);
        Assert.False(IntegrationTypes.TryParse("HttpAuth", out _));
        Assert.False(IntegrationTypes.TryParse(null, out _));
        Assert.True(IntegrationTypes.TryParsePreset("homeAssistant", out var preset));
        Assert.Equal(IntegrationType.HttpAuth, IntegrationTypes.TypeOf(preset));
        Assert.False(IntegrationTypes.TryParsePreset("homeassistant", out _));
    }

    // ----------------------------------------------------------------- glue

    private static List<ValidationIssue> IssuesFor(BaseNode node)
    {
        var def = ValidWorkflowBuilder.Build();
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, node] });
        return result.Issues.Where(i => i.NodeId == node.NodeId && i.Severity == ValidationSeverity.Error).ToList();
    }

    private static TelegramNotificationNode Telegram(Guid? integrationRef) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "tg",
        Ports = [],
        Config = new TelegramConfig { IntegrationRef = integrationRef, ChatId = "-100", Message = "m" }
    };

    private static EmailNotificationNode Email(Guid? integrationRef) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "mail",
        Ports = [],
        Config = new EmailConfig { IntegrationRef = integrationRef, To = "a@example.test", Subject = "s", Body = "b" }
    };

    private static DiscordNotificationNode Discord(Guid? integrationRef) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "discord",
        Ports = [],
        Config = new DiscordConfig { IntegrationRef = integrationRef, Message = "m" }
    };

    private static NtfyNotificationNode Ntfy(Guid? integrationRef) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "ntfy",
        Ports = [],
        Config = new NtfyConfig { IntegrationRef = integrationRef, Message = "m" }
    };

    private static WebhookNotificationNode Webhook(string url, Guid? integrationRef, Dictionary<string, string>? headers = null) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "call",
        Ports = [],
        Config = new WebhookNotificationConfig { Url = url, Method = "POST", IntegrationRef = integrationRef, Headers = headers }
    };
}

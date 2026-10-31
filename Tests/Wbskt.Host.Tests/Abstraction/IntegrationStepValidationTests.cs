using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.Abstraction;

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

    [Fact]
    public void Telegram_message_is_1_to_4096_characters()
    {
        var ok = Telegram(Guid.NewGuid()) with { Config = new TelegramConfig { IntegrationRef = Guid.NewGuid(), ChatId = "-100", Message = new string('a', 4096) } };
        var tooLong = ok with { Config = ok.Config with { Message = new string('a', 4097) } };
        var blank = ok with { Config = ok.Config with { Message = " " } };

        Assert.DoesNotContain(IssuesFor(ok), i => i.Code == "TELEGRAM_MESSAGE_INVALID");
        Assert.Contains(IssuesFor(tooLong), i => i.Code == "TELEGRAM_MESSAGE_INVALID");
        Assert.Contains(IssuesFor(blank), i => i.Code == "TELEGRAM_MESSAGE_INVALID");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("MarkdownV2", true)]
    [InlineData("HTML", true)]
    [InlineData("Markdown", false)]
    [InlineData("html", false)]
    public void Telegram_parse_mode_is_one_telegram_accepts(string? parseMode, bool valid)
    {
        var node = Telegram(Guid.NewGuid()) with { Config = new TelegramConfig { IntegrationRef = Guid.NewGuid(), ChatId = "-100", Message = "m", ParseMode = parseMode } };

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "TELEGRAM_PARSE_MODE_INVALID"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("42", true)]
    [InlineData("0", false)]
    [InlineData("-3", false)]
    [InlineData("12a", false)]
    [InlineData("99999999999", false)]
    public void Telegram_topic_is_an_id(string? threadId, bool valid)
    {
        var node = Telegram(Guid.NewGuid()) with { Config = new TelegramConfig { IntegrationRef = Guid.NewGuid(), ChatId = "-100", Message = "m", MessageThreadId = threadId } };

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "TELEGRAM_THREAD_INVALID"));
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
    public void Discord_message_may_be_left_out_when_there_is_an_embed()
    {
        var node = DiscordWith(new DiscordConfig { IntegrationRef = Guid.NewGuid(), Embeds = [new DiscordEmbed { Title = "Tank low" }] });

        Assert.DoesNotContain(IssuesFor(node), i => i.Code.StartsWith("DISCORD_", StringComparison.Ordinal));
    }

    [Fact]
    public void Discord_embed_needs_something_to_show()
    {
        var node = DiscordWith(new DiscordConfig { IntegrationRef = Guid.NewGuid(), Embeds = [new DiscordEmbed { Color = "#112233" }] });

        Assert.Contains(IssuesFor(node), i => i.Code == "DISCORD_EMBED_INVALID");
    }

    [Theory]
    [InlineData("#E03C31", true)]
    [InlineData("#e03c31", true)]
    [InlineData("E03C31", false)]
    [InlineData("#E03C3", false)]
    [InlineData("#GGGGGG", false)]
    public void Discord_embed_color_is_hex(string color, bool valid)
    {
        var node = DiscordWith(new DiscordConfig { IntegrationRef = Guid.NewGuid(), Embeds = [new DiscordEmbed { Title = "t", Color = color }] });

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "DISCORD_EMBED_INVALID"));
    }

    [Fact]
    public void Discord_embeds_keep_to_discords_limits()
    {
        DiscordEmbed Ok() => new() { Title = "t" };
        var cases = new DiscordConfig[]
        {
            new() { Embeds = Enumerable.Range(0, 11).Select(_ => Ok()).ToList() },
            new() { Embeds = [new DiscordEmbed { Title = new string('a', 257) }] },
            new() { Embeds = [new DiscordEmbed { Description = new string('a', 4097) }] },
            new() { Embeds = [new DiscordEmbed { Footer = new string('a', 2049) }] },
            new() { Embeds = [new DiscordEmbed { Fields = Enumerable.Range(0, 26).Select(_ => new DiscordEmbedField { Name = "n", Value = "v" }).ToList() }] },
            new() { Embeds = [new DiscordEmbed { Fields = [new DiscordEmbedField { Name = " ", Value = "v" }] }] },
            new() { Embeds = [new DiscordEmbed { Fields = [new DiscordEmbedField { Name = "n", Value = new string('a', 1025) }] }] },
            new() { Embeds = [new DiscordEmbed { Title = "t", ImageUrl = "/chart.png" }] },
            new() { Embeds = [new DiscordEmbed { Description = new string('a', 4000) }, new DiscordEmbed { Description = new string('a', 2001) }] }
        };

        foreach (var config in cases)
        {
            Assert.Contains(IssuesFor(DiscordWith(config with { IntegrationRef = Guid.NewGuid() })), i => i.Code == "DISCORD_EMBED_INVALID");
        }
    }

    [Theory]
    [InlineData("1200000000000000002", true)]
    [InlineData("general", false)]
    [InlineData("", false)]
    [InlineData("{{thread}}", true)]
    public void Discord_thread_is_a_discord_id(string threadId, bool valid)
    {
        var node = DiscordWith(new DiscordConfig { IntegrationRef = Guid.NewGuid(), Message = "m", ThreadId = threadId });

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "DISCORD_THREAD_INVALID"));
    }

    [Theory]
    [InlineData("https://example.test/pump.png", true)]
    [InlineData("pump.png", false)]
    [InlineData("ftp://example.test/pump.png", false)]
    [InlineData("{{$shared.avatar}}", true)]
    public void Discord_avatar_is_an_absolute_web_url(string avatarUrl, bool valid)
    {
        var node = DiscordWith(new DiscordConfig { IntegrationRef = Guid.NewGuid(), Message = "m", AvatarUrl = avatarUrl });

        Assert.Equal(!valid, IssuesFor(node).Any(i => i.Code == "DISCORD_AVATAR_URL_INVALID"));
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
    public void A_url_filled_in_from_a_placeholder_is_checked_when_the_step_runs()
    {
        Assert.DoesNotContain(IssuesFor(Webhook("{{$shared.hookUrl}}", null)), i => i.Code == "WEBHOOK_URL_RELATIVE");
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

    [Theory]
    [InlineData(null, false)]
    [InlineData(1, false)]
    [InlineData(60, false)]
    [InlineData(0, true)]
    [InlineData(61, true)]
    [InlineData(-5, true)]
    public void A_webhook_timeout_must_be_one_to_sixty_seconds(int? timeoutSeconds, bool refused)
    {
        var node = Webhook("https://example.test", null) with { Config = new WebhookNotificationConfig { Url = "https://example.test", Method = "POST", TimeoutSeconds = timeoutSeconds } };

        Assert.Equal(refused, IssuesFor(node).Any(i => i.Code == "WEBHOOK_TIMEOUT_INVALID"));
    }

    [Fact]
    public void The_builders_branching_webhook_keeps_its_timeout()
    {
        var def = new Wbskt.Workflow.Builder.WorkflowBuilder("timeout")
            .AddManualTrigger()
            .AddWebhook("GET", "https://example.test", null, scope => scope.OnSuccess(_ => { }), timeoutSeconds: 7)
            .Build();

        Assert.Equal(7, def.Nodes.OfType<WebhookNotificationNode>().Single().Config.TimeoutSeconds);
    }

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

    private static DiscordNotificationNode DiscordWith(DiscordConfig config) => Discord(Guid.NewGuid()) with { Config = config };

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

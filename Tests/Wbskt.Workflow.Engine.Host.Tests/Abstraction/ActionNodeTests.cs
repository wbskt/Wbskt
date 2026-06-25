using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class ActionNodeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SendCommandActionNode_deserialises_with_retry()
    {
        var json = """
            {
                "nodeId": "33333333-3333-3333-3333-333333333333",
                "kind": "action:clientMessage",
                "name": "OpenVent",
                "ports": [{ "portId": "default", "direction": "Output", "label": "Out" }],
                "config": { "clientRef": "$trigger.deviceId", "type": "OpenVent" },
                "retry": { "strategy": "Exponential", "initialDelay": "00:00:01", "factor": 2.0, "maxAttempts": 3, "jitterPct": 10, "retryOn": [] },
                "onFailure": { "outcome": "FailBranch" }
            }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var cmd = Assert.IsType<SendClientMessageNode>(node);
        Assert.Equal("$trigger.deviceId", cmd.Config.ClientRef);
        Assert.Equal("OpenVent", cmd.Config.Type);
        Assert.NotNull(cmd.Retry);
        Assert.Equal(RetryStrategy.Exponential, cmd.Retry!.Strategy);
        Assert.NotNull(cmd.OnFailure);
        Assert.Equal(ErrorOutcome.FailBranch, cmd.OnFailure!.Outcome);
    }

    [Fact]
    public void EmailNotificationNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000011", "kind": "action:email", "name": "Email", "ports": [], "config": { "to": "ops@x", "subject": "Alert", "body": "Test" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var email = Assert.IsType<EmailNotificationNode>(node);
        Assert.Equal("ops@x", email.Config.To);
    }

    [Fact]
    public void WebhookNotificationNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000012", "kind": "action:webhook", "name": "Webhook", "ports": [], "config": { "url": "https://example.com/hook", "method": "POST" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var wh = Assert.IsType<WebhookNotificationNode>(node);
        Assert.Equal("https://example.com/hook", wh.Config.Url);
    }

    [Fact]
    public void TelegramNotificationNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000013", "kind": "action:telegram", "name": "Telegram", "ports": [], "config": { "chatId": "123456", "message": "Hello" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var tg = Assert.IsType<TelegramNotificationNode>(node);
        Assert.Equal("123456", tg.Config.ChatId);
    }

    [Fact]
    public void ToastNotificationNode_deserialises()
    {
        var json = """{ "nodeId": "00000000-0000-0000-0000-000000000014", "kind": "action:toast", "name": "Toast", "ports": [], "config": { "title": "Alert", "message": "Something happened" } }""";
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var toast = Assert.IsType<ToastNotificationNode>(node);
        Assert.Equal("Alert", toast.Config.Title);
    }
}

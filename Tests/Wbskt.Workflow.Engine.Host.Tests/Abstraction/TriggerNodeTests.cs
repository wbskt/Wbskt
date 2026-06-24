using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Enums;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class TriggerNodeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DeviceTriggerNode_deserialises_from_json()
    {
        var json = """
            {
                "nodeId": "11111111-1111-1111-1111-111111111111",
                "kind": "trigger:client",
                "name": "Temp Trigger",
                "ports": [{ "portId": "default", "direction": "Output", "label": "Out" }],
                "config": {
                    "clientRef": "sensor-A",
                    "type": "telemetry",
                    "correlationKey": "$trigger.deviceId",
                    "concurrencyPolicy": "CancelExisting"
                }
            }
            """;

        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);

        var trigger = Assert.IsType<ClientTriggerNode>(node);
        Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), trigger.NodeId);
        Assert.Equal("sensor-A", trigger.Config.ClientRef);
        Assert.Equal("telemetry", trigger.Config.Type);
        Assert.Equal("$trigger.deviceId", trigger.Config.CorrelationKey);
        Assert.Equal(WorkflowConcurrencyPolicy.CancelExisting, trigger.Config.ConcurrencyPolicy);
    }

    [Fact]
    public void DeviceTriggerNode_defaults_concurrencyPolicy_to_Queue_when_omitted()
    {
        var json = """
            {
                "nodeId": "11111111-1111-1111-1111-111111111111",
                "kind": "trigger:client",
                "name": "Trigger",
                "ports": [],
                "config": { "clientRef": "dev-1", "type": "telemetry" }
            }
            """;

        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var trigger = Assert.IsType<ClientTriggerNode>(node);
        Assert.Equal(WorkflowConcurrencyPolicy.Queue, trigger.Config.ConcurrencyPolicy);
    }

    [Fact]
    public void ScheduleTriggerNode_deserialises()
    {
        var json = """
            {
                "nodeId": "00000000-0000-0000-0000-000000000001",
                "kind": "trigger:schedule",
                "name": "Cron Trigger",
                "ports": [{ "portId": "default", "direction": "Output", "label": "Out" }],
                "config": { "cron": "0 6 * * *" }
            }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var sched = Assert.IsType<ScheduleTriggerNode>(node);
        Assert.Equal("0 6 * * *", sched.Config.Cron);
    }

    [Fact]
    public void WebhookTriggerNode_deserialises()
    {
        var json = """
            {
                "nodeId": "00000000-0000-0000-0000-000000000002",
                "kind": "trigger:webhook",
                "name": "Webhook",
                "ports": [{ "portId": "default", "direction": "Output", "label": "Out" }],
                "config": { "path": "/water-start", "method": "POST" }
            }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var webhook = Assert.IsType<WebhookTriggerNode>(node);
        Assert.Equal("/water-start", webhook.Config.Path);
        Assert.Equal("POST", webhook.Config.Method);
    }

    [Fact]
    public void ManualTriggerNode_deserialises()
    {
        var json = """
            {
                "nodeId": "00000000-0000-0000-0000-000000000003",
                "kind": "trigger:manual",
                "name": "Manual",
                "ports": [{ "portId": "default", "direction": "Output", "label": "Out" }],
                "config": { "description": "Start manually" }
            }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var manual = Assert.IsType<ManualTriggerNode>(node);
        Assert.Equal("Start manually", manual.Config.Description);
    }
}

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
                "nodeId": "T",
                "kind": "trigger:device",
                "name": "Temp Trigger",
                "ports": [{ "portId": "out", "direction": "Output", "label": "Out" }],
                "config": {
                    "deviceRef": "sensor-A",
                    "event": "telemetry",
                    "correlationKey": "$trigger.deviceId",
                    "concurrencyPolicy": "CancelExisting"
                }
            }
            """;

        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);

        var trigger = Assert.IsType<DeviceTriggerNode>(node);
        Assert.Equal("T", trigger.NodeId);
        Assert.Equal("sensor-A", trigger.Config.DeviceRef);
        Assert.Equal("telemetry", trigger.Config.Event);
        Assert.Equal("$trigger.deviceId", trigger.Config.CorrelationKey);
        Assert.Equal(WorkflowConcurrencyPolicy.CancelExisting, trigger.Config.ConcurrencyPolicy);
    }

    [Fact]
    public void DeviceTriggerNode_defaults_concurrencyPolicy_to_Queue_when_omitted()
    {
        var json = """
            {
                "nodeId": "T",
                "kind": "trigger:device",
                "name": "Trigger",
                "ports": [],
                "config": { "deviceRef": "dev-1", "event": "telemetry" }
            }
            """;

        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var trigger = Assert.IsType<DeviceTriggerNode>(node);
        Assert.Equal(WorkflowConcurrencyPolicy.Queue, trigger.Config.ConcurrencyPolicy);
    }

    [Fact]
    public void ScheduleTriggerNode_deserialises()
    {
        var json = """
            {
                "nodeId": "S1",
                "kind": "trigger:schedule",
                "name": "Cron Trigger",
                "ports": [{ "portId": "out", "direction": "Output", "label": "Out" }],
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
                "nodeId": "W1",
                "kind": "trigger:webhook",
                "name": "Webhook",
                "ports": [{ "portId": "out", "direction": "Output", "label": "Out" }],
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
                "nodeId": "M1",
                "kind": "trigger:manual",
                "name": "Manual",
                "ports": [{ "portId": "out", "direction": "Output", "label": "Out" }],
                "config": { "description": "Start manually" }
            }
            """;
        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);
        var manual = Assert.IsType<ManualTriggerNode>(node);
        Assert.Equal("Start manually", manual.Config.Description);
    }
}

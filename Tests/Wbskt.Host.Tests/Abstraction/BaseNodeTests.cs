using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Host.Tests.Abstraction;

public class BaseNodeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BaseNode_deserialises_control_logic_kind_as_LogicGateNode()
    {
        var json = """
            {
                "nodeId": "22222222-2222-2222-2222-222222222222",
                "kind": "control:logic",
                "name": "Gate 1",
                "ports": [
                    { "portId": "in", "direction": "Input", "label": "In" },
                    { "portId": "true", "direction": "Output", "label": "True" },
                    { "portId": "false", "direction": "Output", "label": "False" }
                ]
            }
            """;

        var node = JsonSerializer.Deserialize<BaseNode>(json, Options);

        Assert.IsType<LogicGateNode>(node);
        Assert.Equal(new Guid("22222222-2222-2222-2222-222222222222"), node!.NodeId);
        Assert.Equal("control:logic", node.Kind);
        Assert.Equal("Gate 1", node.Name);
        Assert.Equal(3, node.Ports.Count);
    }

    [Fact]
    public void LogicGateNode_roundtrips_json()
    {
        var node = new LogicGateNode { NodeId = new Guid("22222222-2222-2222-2222-222222222222"), Name = "Gate 1", Ports = [new() { PortId = "in", Direction = Wbskt.Workflow.Abstraction.Enums.PortDirection.Input, Label = "In" }], Config = null };
        var json = JsonSerializer.Serialize<BaseNode>(node, Options);
        var back = JsonSerializer.Deserialize<BaseNode>(json, Options);
        Assert.IsType<LogicGateNode>(back);
    }

    [Fact]
    public void The_wire_shape_writes_kind_first()
    {
        // The definition JSON is stored and exported; its shape must not move when the converter's
        // kind lookup does. "kind" is written first, then the model's own properties in order.
        var node = new EndNode { NodeId = new Guid("33333333-3333-3333-3333-333333333333"), Name = "stop", Ports = [In] };

        string json = JsonSerializer.Serialize<BaseNode>(node, Options);

        Assert.Equal(
            """{"kind":"control:end","nodeId":"33333333-3333-3333-3333-333333333333","name":"stop","ports":[{"portId":"in","direction":"Input","label":"In"}],"posX":0,"posY":0}""",
            json);
    }

    [Fact]
    public void There_is_a_sample_for_every_described_kind()
    {
        Assert.Equal(NodeKinds.All.Select(d => d.Kind).Order(StringComparer.Ordinal), Samples.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(DescribedKinds))]
    public void Every_described_kind_roundtrips_through_the_converter(string kind)
    {
        Assert.True(NodeKinds.TryGet(kind, out NodeKindDescriptor? descriptor));
        BaseNode node = Samples[kind];

        string json = JsonSerializer.Serialize(node, Options);
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            JsonProperty first = doc.RootElement.EnumerateObject().First();
            Assert.Equal("kind", first.Name);
            Assert.Equal(kind, first.Value.GetString());
        }

        BaseNode? back = JsonSerializer.Deserialize<BaseNode>(json, Options);

        Assert.NotNull(back);
        Assert.Equal(descriptor.ModelType, back.GetType());
        Assert.Equal(kind, back.Kind);
        Assert.Equal(json, JsonSerializer.Serialize(back, Options));
    }

    [Fact]
    public void An_undescribed_kind_is_rejected_by_the_converter()
    {
        var json = """{ "kind": "action:undescribed", "nodeId": "22222222-2222-2222-2222-222222222222", "name": "n", "ports": [] }""";

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BaseNode>(json, Options));
        Assert.Contains("action:undescribed", exception.Message);
    }

    public static TheoryData<string> DescribedKinds()
    {
        var data = new TheoryData<string>();
        foreach (NodeKindDescriptor descriptor in NodeKinds.All)
        {
            data.Add(descriptor.Kind);
        }

        return data;
    }

    private static readonly PortDefinition In = new() { PortId = "in", Direction = PortDirection.Input, Label = "In" };
    private static readonly PortDefinition Out = new() { PortId = "default", Direction = PortDirection.Output, Label = "Out" };
    private static readonly Guid Id = new("44444444-4444-4444-4444-444444444444");

    // One populated node per kind, so the round trip carries real config rather than nulls.
    private static readonly Dictionary<string, BaseNode> Samples = new BaseNode[]
    {
        new ClientTriggerNode { NodeId = Id, Name = "t", Ports = [Out], Config = new ClientTriggerConfig { ClientRef = "dev-1", Type = "telemetry" } },
        new ClientPresenceTriggerNode { NodeId = Id, Name = "t", Ports = [Out], Config = new ClientPresenceTriggerConfig { ClientRef = "dev-1" } },
        new ScheduleTriggerNode { NodeId = Id, Name = "t", Ports = [Out], Config = new ScheduleTriggerConfig { Cron = "0 * * * *" } },
        new WebhookTriggerNode { NodeId = Id, Name = "t", Ports = [Out], Config = new WebhookTriggerConfig { Path = "orders", Method = "POST" } },
        new ManualTriggerNode { NodeId = Id, Name = "t", Ports = [Out], Config = new ManualTriggerConfig { Description = "d" } },
        new LogicGateNode { NodeId = Id, Name = "c", Ports = [In], Config = new LogicGateConfig { Condition = LogicConditionJsonConverter.FromLegacyString("$trigger.temperature > 35") } },
        new ForEachNode { NodeId = Id, Name = "c", Ports = [In], Config = new ForEachConfig { Collection = "$trigger.items" } },
        new ParallelForEachNode { NodeId = Id, Name = "c", Ports = [In], Config = new ParallelForEachConfig { Collection = "$trigger.items" } },
        new JoinNode { NodeId = Id, Name = "c", Ports = [In, Out], Config = new JoinConfig { Mode = JoinMode.All } },
        new ForkNode { NodeId = Id, Name = "c", Ports = [In], Config = new ForkConfig { Branches = ["a", "b"] } },
        new DelayNode { NodeId = Id, Name = "c", Ports = [In, Out], Config = new DelayConfig { Duration = TimeSpan.FromSeconds(5) } },
        new VariableNode { NodeId = Id, Name = "c", Ports = [In, Out], Config = new VariableConfig { Scope = VariableScope.Local, Op = VariableOperation.Set, Var = "x" } },
        new SubWorkflowNode { NodeId = Id, Name = "c", Ports = [In, Out], Config = new SubWorkflowConfig { WorkflowRefId = Id } },
        new WaitForHttpNode { NodeId = Id, Name = "c", Ports = [In, Out], Config = new WaitForHttpConfig { Ttl = TimeSpan.FromMinutes(1) } },
        new AwaitSignalNode { NodeId = Id, Name = "c", Ports = [In, Out], Config = new AwaitSignalConfig { SignalName = "go", Ttl = TimeSpan.FromMinutes(1) } },
        new FailRunNode { NodeId = Id, Name = "c", Ports = [In], Config = new FailRunConfig() },
        new EndNode { NodeId = Id, Name = "c", Ports = [In] },
        new SendClientMessageNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new SendClientMessageConfig { ClientRef = "dev-1", Type = "cmd", AwaitAck = true } },
        new EmailNotificationNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new EmailConfig { To = "a@b", Subject = "s", Body = "b" } },
        new WebhookNotificationNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new WebhookNotificationConfig { Url = "https://example.test/hook", Method = "POST" } },
        new TelegramNotificationNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new TelegramConfig { ChatId = "1", Message = "m" } },
        new ToastNotificationNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new ToastConfig { Title = "t", Message = "m" } },
        new DiscordNotificationNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new DiscordConfig { Message = "m" } },
        new NtfyNotificationNode { NodeId = Id, Name = "a", Ports = [In, Out], Config = new NtfyConfig { Message = "m" } },
    }.ToDictionary(node => node.Kind, StringComparer.Ordinal);
}

using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class BaseNodeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BaseNode_deserialises_control_logic_kind_as_LogicGateNode()
    {
        var json = """
            {
                "nodeId": "G1",
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
        Assert.Equal("G1", node!.NodeId);
        Assert.Equal("control:logic", node.Kind);
        Assert.Equal("Gate 1", node.Name);
        Assert.Equal(3, node.Ports.Count);
    }

    [Fact]
    public void LogicGateNode_roundtrips_json()
    {
        var node = new LogicGateNode("G1", "Gate 1",
            [new("in", Wbskt.Workflow.Abstraction.Enums.PortDirection.Input, "In")],
            null);
        var json = JsonSerializer.Serialize<BaseNode>(node, Options);
        var back = JsonSerializer.Deserialize<BaseNode>(json, Options);
        Assert.IsType<LogicGateNode>(back);
    }
}

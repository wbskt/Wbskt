using System.Text.Json;
using System.Reflection;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class WorkflowDefinitionTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static string LoadFixture()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return File.ReadAllText(Path.Combine(dir, "Abstraction", "Fixtures", "greenhouse-workflow.json"));
    }

    [Fact]
    public void Greenhouse_workflow_deserialises_correct_node_count()
    {
        var json = LoadFixture();
        var def = JsonSerializer.Deserialize<WorkflowDefinition>(json, Options);

        Assert.NotNull(def);
        Assert.Equal(9, def!.Nodes.Count);
        Assert.Equal(8, def.Edges.Count);
        Assert.Single(def.SharedVariableSchema);
    }

    [Fact]
    public void Greenhouse_workflow_roundtrips_without_loss()
    {
        var json = LoadFixture();
        var def = JsonSerializer.Deserialize<WorkflowDefinition>(json, Options);
        var json2 = JsonSerializer.Serialize(def, Options);
        var def2 = JsonSerializer.Deserialize<WorkflowDefinition>(json2, Options);

        Assert.Equal(def!.Nodes.Count, def2!.Nodes.Count);
        Assert.Equal(def.Edges.Count, def2.Edges.Count);
        Assert.Equal(def.Name, def2.Name);
        Assert.Equal(def.Version, def2.Version);
    }

    [Fact]
    public void Greenhouse_nodes_are_correctly_typed()
    {
        var json = LoadFixture();
        var def = JsonSerializer.Deserialize<WorkflowDefinition>(json, Options);

        var trigger = def!.Nodes.OfType<ClientTriggerNode>().Single();
        Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), trigger.NodeId);
        Assert.Equal("CancelExisting", trigger.Config.ConcurrencyPolicy.ToString());

        var logicNodes = def.Nodes.OfType<LogicGateNode>().ToList();
        Assert.Equal(3, logicNodes.Count);

        var delay = def.Nodes.OfType<DelayNode>().Single();
        Assert.Equal(TimeSpan.FromMinutes(10), delay.Config!.Duration);

        var varNode = def.Nodes.OfType<VariableNode>().Single();
        Assert.Equal("smsToday", varNode.Config!.Var);
    }
}


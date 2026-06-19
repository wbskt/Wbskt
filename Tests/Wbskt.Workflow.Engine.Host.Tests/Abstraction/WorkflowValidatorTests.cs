using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Validation;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class WorkflowValidatorTests
{
    private static readonly WorkflowValidator Validator = new();

    [Fact]
    public void Validate_returns_error_when_edge_references_unknown_node()
    {
        var def = ValidWorkflowBuilder.Build() with
        {
            Edges = [new Edge((Guid.NewGuid(), "out"), (Guid.NewGuid(), "in"))]
        };
        var result = Validator.Validate(def);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_NODE");
    }

    [Fact]
    public void Validate_returns_error_when_edge_references_unknown_port()
    {
        var def = ValidWorkflowBuilder.Build();
        var existingNode = def.Nodes.First();
        var badEdge = new Edge((existingNode.NodeId, "nonexistent-port"), (existingNode.NodeId, "in"));
        var newDef = def with { Edges = [badEdge] };
        var result = Validator.Validate(newDef);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_PORT");
    }


    [Fact]
    public void Validate_returns_error_when_edge_references_unknown_target_port()
    {
        var def = ValidWorkflowBuilder.Build();
        var fromNode = def.Nodes.First(n => n.NodeId == ValidWorkflowBuilder.Trigger1Id);
        var badEdge = new Edge((fromNode.NodeId, "out"), (ValidWorkflowBuilder.Logic1Id, "missing-in"));
        var newDef = def with { Edges = [badEdge] };
        var result = Validator.Validate(newDef);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "EDGE_UNKNOWN_PORT" && i.NodeId == ValidWorkflowBuilder.Logic1Id);
    }

    [Fact]
    public void Validate_returns_error_when_node_ids_are_duplicated()
    {
        var def = ValidWorkflowBuilder.Build();
        var duplicate = new LogicGateNode(ValidWorkflowBuilder.Logic1Id, "Duplicate", [], new LogicGateConfig("true"));
        var defWithDuplicate = def with { Nodes = [..def.Nodes, duplicate] };
        var result = Validator.Validate(defWithDuplicate);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "DUPLICATE_NODE_ID" && i.NodeId == ValidWorkflowBuilder.Logic1Id);
    }

    [Fact]
    public void Validate_returns_error_when_workflowRefId_is_empty()
    {
        var def = ValidWorkflowBuilder.Build() with { WorkflowRefId = Guid.Empty };
        var result = Validator.Validate(def);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "INVALID_WORKFLOW_REF_ID");
    }

    [Fact]
    public void Validate_returns_error_when_version_less_than_1()
    {
        var def = ValidWorkflowBuilder.Build() with { Version = 0 };
        var result = Validator.Validate(def);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Code == "INVALID_VERSION");
    }

    [Fact]
    public void Validate_returns_warning_when_no_trigger_nodes()
    {
        var logicNode = new LogicGateNode(new Guid("aaaaaaaa-0000-0000-0000-000000000004"), "Logic", [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("true", PortDirection.Output, "True"), new PortDefinition("false", PortDirection.Output, "False")], new LogicGateConfig("true"));
        var defNoTriggers = ValidWorkflowBuilder.Build() with { Nodes = [logicNode], Edges = [] };
        var result = Validator.Validate(defNoTriggers);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Warning && i.Code == "NO_TRIGGERS");
        Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validate_returns_warning_for_orphan_node()
    {
        var def = ValidWorkflowBuilder.Build();
        var orphanId = new Guid("aaaaaaaa-0000-0000-0000-000000000003");
        var orphan = new LogicGateNode(orphanId, "Orphan", [new PortDefinition("in", PortDirection.Input, "In"), new PortDefinition("true", PortDirection.Output, "True"), new PortDefinition("false", PortDirection.Output, "False")], new LogicGateConfig("true"));
        var defWithOrphan = def with { Nodes = [..def.Nodes, orphan] };
        var result = Validator.Validate(defWithOrphan);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Warning && i.Code == "ORPHAN_NODE" && i.NodeId == orphanId);
    }
}

public static class ValidWorkflowBuilder
{
    public static readonly Guid Trigger1Id = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid Logic1Id = new("aaaaaaaa-0000-0000-0000-000000000002");

    public static WorkflowDefinition Build()
    {
        var trigger = new DeviceTriggerNode(
            Trigger1Id, "Trigger",
            [new PortDefinition("default", PortDirection.Output, "Out")],
            new Wbskt.Workflow.Abstraction.Models.Triggers.DeviceTriggerConfig("dev-1", "telemetry"));

        var logic = new LogicGateNode(
            Logic1Id, "Gate",
            [
                new PortDefinition("in", PortDirection.Input, "In"),
                new PortDefinition("true", PortDirection.Output, "True"),
                new PortDefinition("false", PortDirection.Output, "False")
            ],
            new LogicGateConfig("$trigger.temperature > 35"));

        var edge = new Edge((Trigger1Id, "out"), (Logic1Id, "in"));

        return new WorkflowDefinition(
            WorkflowRefId: Guid.NewGuid(),
            Version: 1,
            WorkspaceId: 1,
            Name: "Test Workflow",
            Description: null,
            IsEnabled: true,
            Nodes: [trigger, logic],
            Edges: [edge],
            SharedVariableSchema: [],
            CreatedAt: DateTime.UtcNow,
            PublishedBy: 1);
    }
}


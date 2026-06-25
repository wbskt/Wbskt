using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Validation;

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
        var duplicate = new LogicGateNode { NodeId = ValidWorkflowBuilder.Logic1Id, Name = "Duplicate", Ports = [], Config = new LogicGateConfig { Condition = "true" } };
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
        var logicNode = new LogicGateNode { NodeId = new Guid("aaaaaaaa-0000-0000-0000-000000000004"), Name = "Logic", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = "true" } };
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
        var orphan = new LogicGateNode { NodeId = orphanId, Name = "Orphan", Ports = [new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" }, new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" }, new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }], Config = new LogicGateConfig { Condition = "true" } };
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
        var trigger = new ClientTriggerNode { NodeId = Trigger1Id, Name = "Trigger", Ports = [new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Out" }], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ClientTriggerConfig { ClientRef = "dev-1", Type = "telemetry" } };

        var logic = new LogicGateNode { NodeId = Logic1Id, Name = "Gate", Ports = [
                new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" },
                new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" },
                new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" }
            ], Config = new LogicGateConfig { Condition = "$trigger.temperature > 35" } };

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


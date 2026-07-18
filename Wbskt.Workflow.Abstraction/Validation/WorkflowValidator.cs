using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

namespace Wbskt.Workflow.Abstraction.Validation;

public sealed class WorkflowValidator
{
    public ValidationResult Validate(WorkflowDefinition? definition)
    {
        var issues = new List<ValidationIssue>();
        if (definition is null)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "NULL_DEFINITION", "Workflow definition must not be null."));
            return new ValidationResult(issues);
        }
        ValidateShape(definition, issues);
        ValidateDuplicateNodeIds(definition, issues);
        ValidateEdges(definition, issues);
        ValidatePorts(definition, issues);
        WarnIfNoTriggers(definition, issues);
        WarnIfOrphans(definition, issues);
        return new ValidationResult(issues);
    }

    private static void ValidateShape(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        if (def.WorkflowRefId == Guid.Empty)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "INVALID_WORKFLOW_REF_ID", "WorkflowRefId must not be empty."));
        }

        if (def.Version < 1)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "INVALID_VERSION", "Version must be >= 1."));
        }
    }

    private static void ValidateDuplicateNodeIds(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var duplicateNodeIds = def.Nodes
            .GroupBy(n => n.NodeId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var nodeId in duplicateNodeIds)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "DUPLICATE_NODE_ID", $"Multiple nodes have the same NodeId '{nodeId}'.", nodeId));
        }
    }

    private static void ValidateEdges(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var nodeMap = def.Nodes
            .GroupBy(n => n.NodeId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var edge in def.Edges)
        {
            if (!nodeMap.TryGetValue(edge.From.NodeId, out var fromNode))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_NODE", $"Edge references unknown source node '{edge.From.NodeId}'."));
                continue;
            }

            if (!nodeMap.TryGetValue(edge.To.NodeId, out var toNode))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_NODE", $"Edge references unknown target node '{edge.To.NodeId}'."));
                continue;
            }

            var fromPortExists = fromNode.Ports.Any(p => p.PortId == edge.From.PortId);
            if (!fromPortExists)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_PORT", $"Edge references unknown port '{edge.From.PortId}' on node '{edge.From.NodeId}'.", edge.From.NodeId));
            }

            var toPortExists = toNode.Ports.Any(p => p.PortId == edge.To.PortId);
            if (!toPortExists)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "EDGE_UNKNOWN_PORT", $"Edge references unknown port '{edge.To.PortId}' on node '{edge.To.NodeId}'.", edge.To.NodeId));
            }
        }
    }

    private static void WarnIfNoTriggers(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var hasTrigger = def.Nodes.Any(n => n is ClientTriggerNode or ScheduleTriggerNode or WebhookTriggerNode or ManualTriggerNode);

        if (!hasTrigger)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NO_TRIGGERS", "Workflow has no trigger nodes; it can never start a run."));
        }
    }

    private static void WarnIfOrphans(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var connectedNodeIds = new HashSet<Guid>();

        foreach (var edge in def.Edges)
        {
            connectedNodeIds.Add(edge.From.NodeId);
            connectedNodeIds.Add(edge.To.NodeId);
        }

        foreach (var node in def.Nodes)
        {
            if (!connectedNodeIds.Contains(node.NodeId))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, "ORPHAN_NODE", $"Node '{node.NodeId}' ('{node.Name}') has no inbound or outbound edges.", node.NodeId));
            }
        }
    }

    private static void ValidatePorts(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        foreach (var node in def.Nodes)
        {
            var expectedPorts = GetExpectedPorts(node);
            
            // Check for missing ports
            foreach (var expected in expectedPorts)
            {
                if (!node.Ports.Any(p => p.PortId == expected.PortId && p.Direction == expected.Direction))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "MISSING_PORT", $"Node '{node.NodeId}' of kind '{node.Kind}' is missing expected {expected.Direction} port '{expected.PortId}'."));
                }
            }

            // Check for extra ports
            foreach (var actual in node.Ports)
            {
                if (!expectedPorts.Any(p => p.PortId == actual.PortId && p.Direction == actual.Direction))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "EXTRA_PORT", $"Node '{node.NodeId}' of kind '{node.Kind}' defines an unexpected {actual.Direction} port '{actual.PortId}'."));
                }
            }
        }
    }

    private static IReadOnlyCollection<PortDefinition> GetExpectedPorts(Models.Nodes.BaseNode node)
    {
        var inputIn = new PortDefinition { PortId = "in", Direction = PortDirection.Input, Label = "In" };
        var outputDefault = new PortDefinition { PortId = "default", Direction = PortDirection.Output, Label = "Default" };
        var outputTrue = new PortDefinition { PortId = "true", Direction = PortDirection.Output, Label = "True" };
        var outputFalse = new PortDefinition { PortId = "false", Direction = PortDirection.Output, Label = "False" };
        var outputBody = new PortDefinition { PortId = "body", Direction = PortDirection.Output, Label = "Body" };
        var outputDone = new PortDefinition { PortId = "done", Direction = PortDirection.Output, Label = "Done" };
        var outputEmpty = new PortDefinition { PortId = "empty", Direction = PortDirection.Output, Label = "Empty" };

        if (node.Kind.StartsWith("trigger:"))
        {
            return [outputDefault];
        }
        
        if (node.Kind.StartsWith("action:"))
        {
            if (node.Kind == "action:webhook")
            {
                return [inputIn, outputDefault, new PortDefinition { PortId = "error", Direction = PortDirection.Output, Label = "Error" }
                ];
            }
            return [inputIn, outputDefault];
        }

        switch (node.Kind)
        {
            case Models.Nodes.NodeKind.ControlLogic:
                return [inputIn, outputTrue, outputFalse];
            
            case Models.Nodes.NodeKind.ControlForEach:
                return [inputIn, outputBody, outputDone];

            case Models.Nodes.NodeKind.ControlParallelForEach:
                return [inputIn, outputBody, outputEmpty];
            
            case Models.Nodes.NodeKind.ControlFork:
                var forkOutputs = new List<PortDefinition> { inputIn };
                if (node is Models.Nodes.Controls.ForkNode { Config: not null } forkNode)
                {
                    foreach (var branch in forkNode.Config.Branches)
                    {
                        forkOutputs.Add(new PortDefinition { PortId = branch, Direction = PortDirection.Output, Label = branch });
                    }
                }
                return forkOutputs;
                
            case Models.Nodes.NodeKind.ControlAwaitSignal:
                if (node is Models.Nodes.Controls.AwaitSignalNode awaitNode && awaitNode.Config?.Ttl is not null)
                {
                    return [inputIn, outputDefault, new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }];
                }
                return [inputIn, outputDefault];

            case Models.Nodes.NodeKind.ControlWaitForHttp:
                // WaitForHttp always has a mandatory Ttl, so the timeout port is always present.
                return [inputIn, outputDefault, new PortDefinition { PortId = "timeout", Direction = PortDirection.Output, Label = "Timeout" }];
                
            case Models.Nodes.NodeKind.ControlEnd:
            case Models.Nodes.NodeKind.ControlFailRun:
                return [inputIn];

            default:
                // Delay, Join, Variable, SubWorkflow
                return [inputIn, outputDefault];
        }
    }
}



using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

namespace Wbskt.Workflow.Abstraction.Validation;

public sealed class WorkflowValidator
{
    public ValidationResult Validate(WorkflowDefinition definition)
    {
        var issues = new List<ValidationIssue>();
        ValidateShape(definition, issues);
        ValidateDuplicateNodeIds(definition, issues);
        ValidateEdges(definition, issues);
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
        var hasTrigger = def.Nodes.Any(n => n is DeviceTriggerNode or ScheduleTriggerNode or WebhookTriggerNode or ManualTriggerNode);

        if (!hasTrigger)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, "NO_TRIGGERS", "Workflow has no trigger nodes; it can never start a run."));
        }
    }

    private static void WarnIfOrphans(WorkflowDefinition def, List<ValidationIssue> issues)
    {
        var connectedNodeIds = new HashSet<string>();

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
}



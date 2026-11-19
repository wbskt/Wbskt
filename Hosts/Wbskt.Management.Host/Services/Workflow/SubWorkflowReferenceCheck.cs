using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// The half of checking a definition's "Run workflow" steps that needs the workspace: the workflow each
/// names is one of this workspace's, with a published version. Another workspace's workflow, or one
/// that is gone, is an error that reads the same either way, so the check confirms nothing about refs
/// outside the workspace. A target with no manual trigger is a warning: the step cannot start it until
/// one is published. <see cref="WorkflowValidator"/> has already checked that a ref is set and is not
/// the workflow itself. This runs after the validator on publish, enable and draft validation, beside
/// <see cref="ClientReferenceCheck"/>.
/// </summary>
public sealed class SubWorkflowReferenceCheck(IWorkflowDefinitionProvider definitions)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ValidationIssue>> CheckAsync(int workspaceId, WorkflowDefinition? definition, CancellationToken ct)
    {
        var steps = (definition?.Nodes ?? [])
            .OfType<SubWorkflowNode>()
            .Where(node => node.Config is { } config && config.WorkflowRefId != Guid.Empty)
            .ToList();
        if (steps.Count == 0)
        {
            return [];
        }

        // A definition names a handful of child workflows, so one read each.
        var hasManualTrigger = new Dictionary<Guid, bool?>();
        foreach (Guid childRef in steps.Select(step => step.Config!.WorkflowRefId).Distinct())
        {
            var row = await definitions.FindCurrentByRefIdAsync(childRef, ct);
            hasManualTrigger[childRef] = row is null || row.WorkspaceId != workspaceId ? null : HasManualTrigger(row.DefinitionJson);
        }

        var issues = new List<ValidationIssue>();
        foreach (SubWorkflowNode step in steps)
        {
            Guid childRef = step.Config!.WorkflowRefId;
            switch (hasManualTrigger[childRef])
            {
                case null:
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "SUBWORKFLOW_TARGET_NOT_FOUND",
                        $"Step '{step.Name}' runs workflow {childRef}, which this workspace does not have; it may have been deleted.",
                        step.NodeId,
                        "config.workflowRefId"));
                    break;
                case false:
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "SUBWORKFLOW_TARGET_NOT_MANUAL",
                        $"Step '{step.Name}' runs workflow {childRef}, which has no manual trigger, so the step cannot start it until one is published.",
                        step.NodeId,
                        "config.workflowRefId"));
                    break;
            }
        }

        return issues;
    }

    private static bool HasManualTrigger(string definitionJson)
    {
        WorkflowDefinition? child = JsonSerializer.Deserialize<WorkflowDefinition>(definitionJson, SerializerOptions);
        return child?.Nodes?.OfType<ManualTriggerNode>().Any() == true;
    }
}

using System.Text.Json;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Integrations;

/// <summary>What a definition's integration steps come to in a workspace: the problems, and the refs to record.</summary>
public sealed record IntegrationReferences(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<WorkflowIntegrationRef> Refs);

/// <summary>
/// The half of validating a definition that needs the workspace: every integration a step names is
/// one of the workspace's, of the type the step sends through, and (for Call a URL) used where its
/// credential may go. <see cref="WorkflowValidator"/> checks everything the definition alone can
/// answer; this runs after it on publish, rollback, reinstate and draft validation. The publish
/// procedure checks existence again under lock, so a delete racing a publish cannot slip between.
/// </summary>
public sealed class IntegrationReferenceCheck(IIntegrationProvider integrations)
{
    public async Task<IntegrationReferences> CheckAsync(int workspaceId, WorkflowDefinition? definition, CancellationToken ct)
    {
        var steps = definition?.Nodes
            .Where(n => n is IIntegrationStep { IntegrationRef: { } r } && r != Guid.Empty)
            .ToList() ?? [];
        if (steps.Count == 0)
        {
            return new IntegrationReferences([], []);
        }

        // A workspace holds a few dozen at most, so one read beats one per step.
        var owned = (await integrations.GetAllAsync(workspaceId, null, ct)).ToDictionary(i => i.RefId);
        var issues = new List<ValidationIssue>();
        var refs = new List<WorkflowIntegrationRef>();

        foreach (var node in steps)
        {
            Guid reference = ((IIntegrationStep)node).IntegrationRef!.Value;
            IntegrationType expected = IntegrationTypes.ForStepKind(node.Kind)!.Value;

            if (!owned.TryGetValue(reference, out var integration))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "INTEGRATION_NOT_FOUND",
                    $"Step '{node.Name}' names integration {reference}, which this workspace does not have; it may have been deleted.",
                    node.NodeId));
                continue;
            }

            if (integration.Type != expected)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "INTEGRATION_WRONG_TYPE",
                    $"Step '{node.Name}' sends through {IntegrationTypes.NameOf(expected)}, but '{integration.Name}' is a {IntegrationTypes.NameOf(integration.Type)} integration.",
                    node.NodeId));
                continue;
            }

            if (node is WebhookNotificationNode webhook)
            {
                CheckCallAUrl(webhook, integration, issues);
            }

            refs.Add(new WorkflowIntegrationRef(node.NodeId, node.Name, reference));
        }

        return new IntegrationReferences(issues, refs);
    }

    /// <summary>
    /// The credential only goes to the integration's own origin, and in its own header. The engine
    /// refuses both at run time too; catching them here means the author hears at publish, not from
    /// a failed run.
    /// </summary>
    private static void CheckCallAUrl(WebhookNotificationNode node, Integration integration, List<ValidationIssue> issues)
    {
        string? baseUrl = null;
        string authHeader = "Authorization";
        using (var config = JsonDocument.Parse(integration.ConfigJson))
        {
            if (config.RootElement.TryGetProperty("baseUrl", out var b))
            {
                baseUrl = b.GetString();
            }

            if (config.RootElement.TryGetProperty("mode", out var m) && m.GetString() == "header"
                && config.RootElement.TryGetProperty("headerName", out var h) && h.GetString() is { } headerName)
            {
                authHeader = headerName;
            }
        }

        if (baseUrl is not null
            && Uri.TryCreate(node.Config.Url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https"
            && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && Uri.Compare(absolute, baseUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "WEBHOOK_ORIGIN_MISMATCH",
                $"Step '{node.Name}' calls {absolute.GetLeftPart(UriPartial.Authority)}, but '{integration.Name}' is for {baseUri.GetLeftPart(UriPartial.Authority)}. Use a URL on that address, or a relative one.",
                node.NodeId));
        }

        if (node.Config.Headers?.Keys.Any(k => k.Equals(authHeader, StringComparison.OrdinalIgnoreCase)) == true)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "WEBHOOK_HEADER_CONFLICT",
                $"Step '{node.Name}' sets '{authHeader}', which '{integration.Name}' puts its credential in.",
                node.NodeId));
        }
    }
}

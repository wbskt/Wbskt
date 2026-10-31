using Wbskt.Management.Host.Providers;
using Wbskt.Protocol;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// The half of checking a definition's devices that needs the workspace: each device a step sends to
/// or a trigger listens for is one of the workspace's, and is registered. A missing one is an error:
/// every command to it fails and its triggers never fire. One that is pending, revoked or rejected is a
/// warning, since its status can change back. <see cref="WorkflowValidator"/> has already checked that
/// each reference is a device id; this runs after it on publish and draft validation.
/// </summary>
public sealed class DeviceReferenceCheck(IClientProvider clients)
{
    public async Task<IReadOnlyList<ValidationIssue>> CheckAsync(int workspaceId, WorkflowDefinition? definition, CancellationToken ct)
    {
        var references = (definition?.Nodes ?? [])
            .Select(node => (Node: node, Ref: DeviceRef(node)))
            .Where(r => r.Ref is not null)
            .Select(r => (r.Node, Ref: r.Ref!.Value))
            .ToList();
        if (references.Count == 0)
        {
            return [];
        }

        // A definition names a handful of devices, so one read each is cheaper than listing the workspace.
        var found = new Dictionary<Guid, Models.Client?>();
        foreach (Guid clientRef in references.Select(r => r.Ref).Distinct())
        {
            var client = await clients.FindDetailByRefIdAsync(clientRef, ct);
            found[clientRef] = client?.WorkspaceId == workspaceId ? client : null;
        }

        var issues = new List<ValidationIssue>();
        foreach (var (node, clientRef) in references)
        {
            var client = found[clientRef];
            if (client is null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "CLIENT_NOT_FOUND",
                    $"Step '{node.Name}' names device {clientRef}, which this workspace does not have; it may have been deleted.",
                    node.NodeId,
                    "config.clientRef"));
            }
            else if (client.Status != ClientStatus.Registered)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "CLIENT_NOT_REGISTERED",
                    $"Step '{node.Name}' names '{client.Name}', which is {client.Status.ToString().ToLowerInvariant()}; it cannot connect until it is registered again.",
                    node.NodeId,
                    "config.clientRef"));
            }
        }

        return issues;
    }

    private static Guid? DeviceRef(BaseNode node)
    {
        string? reference = node switch
        {
            SendClientMessageNode send => send.Config?.ClientRef,
            ClientTriggerNode trigger => trigger.Config?.ClientRef,
            ClientPresenceTriggerNode presence => presence.Config?.ClientRef,
            _ => null
        };

        return Guid.TryParse(reference, out Guid clientRef) ? clientRef : null;
    }
}

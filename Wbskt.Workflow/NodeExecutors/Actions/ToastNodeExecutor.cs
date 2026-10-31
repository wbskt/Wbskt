using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Raises a toast to everyone watching the workspace. A toast has no device target - unlike
/// <see cref="CommandNodeExecutor"/> it addresses the workspace's dashboard connections - so it
/// publishes a workspace-scoped event and lets the management host fan it out over SignalR.
/// </summary>
internal sealed class ToastNodeExecutor(IToastPublisher publisher, ActionTemplates templates) : INodeExecutor
{
    public string Kind => NodeKind.ActionToast;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        ToastNotificationNode node = (ToastNotificationNode)ctx.Node;
        string title = await templates.TextAsync(ctx, "title", node.Config.Title, ct);
        string message = await templates.TextAsync(ctx, "message", node.Config.Message, ct);

        try
        {
            await publisher.PublishToastAsync(
                ctx.Branch.WorkflowDefinitionRefId,
                ctx.Branch.WorkflowDefinitionId,
                ctx.Branch.RunRefId,
                ctx.Branch.WorkspaceId,
                title,
                message,
                ct);

            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
        }
        catch (Exception ex)
        {
            // Transient: the bus being briefly unavailable is worth a retry.
            return new NodeExecutionResult.Fail("TOAST_PUBLISH_ERROR", ex.Message, true, ex);
        }
    }
}

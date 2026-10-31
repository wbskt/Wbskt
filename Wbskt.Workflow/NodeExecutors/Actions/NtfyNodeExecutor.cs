using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>Publishes a push notification to the ntfy topic of the integration the step names.</summary>
internal sealed class NtfyNodeExecutor(IIntegrationResolver resolver, NtfyChannel channel, ActionTemplates templates) : INodeExecutor
{
    public string Kind => NodeKind.ActionNtfy;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (NtfyNotificationNode)ctx.Node;
        NtfyConfig config = node.Config
            ?? throw new InvalidOperationException($"ntfy node {node.NodeId} is missing config.");

        var resolution = await resolver.ResolveAsync(ctx.Branch, config.IntegrationRef, IntegrationType.Ntfy, ct);
        if (resolution.Failure is not null)
        {
            return resolution.Failure;
        }

        var integration = resolution.Integration!;
        string message = await templates.TextAsync(ctx, "message", config.Message, ct);
        string? title = await templates.LineOrNullAsync(ctx, "title", config.Title, ct);
        List<string>? tags = null;
        for (int i = 0; i < (config.Tags?.Count ?? 0); i++)
        {
            (tags ??= []).Add(await templates.LineAsync(ctx, $"tags[{i}]", config.Tags![i], ct));
        }

        var result = await channel.PublishAsync(integration, message, title, config.Priority, tags, ct);
        if (!result.Ok)
        {
            return new NodeExecutionResult.Fail(result.Code!, result.Message!, result.Retryable, null);
        }

        await resolver.MarkUsedAsync(integration, ct);
        return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(result.Status ?? 200)
        });
    }
}

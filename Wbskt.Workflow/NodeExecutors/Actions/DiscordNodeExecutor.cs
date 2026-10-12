using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>Posts a message to a Discord channel through the webhook integration the step names.</summary>
internal sealed class DiscordNodeExecutor(IIntegrationResolver resolver, DiscordChannel channel) : INodeExecutor
{
    public string Kind => NodeKind.ActionDiscord;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (DiscordNotificationNode)ctx.Node;
        DiscordConfig config = node.Config
            ?? throw new InvalidOperationException($"Discord node {node.NodeId} is missing config.");

        var resolution = await resolver.ResolveAsync(ctx.Branch, config.IntegrationRef, IntegrationType.Discord, ct);
        if (resolution.Failure is not null)
        {
            return resolution.Failure;
        }

        var integration = resolution.Integration!;
        var result = await channel.SendAsync(integration, config.Message, config.Username, ct);
        if (!result.Ok)
        {
            return new NodeExecutionResult.Fail(result.Code!, result.Message!, result.Retryable, null);
        }

        await resolver.MarkUsedAsync(integration, ct);
        return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(result.Status ?? 204)
        });
    }
}

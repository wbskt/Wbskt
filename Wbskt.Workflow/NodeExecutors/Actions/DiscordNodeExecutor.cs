using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Posts a message to a Discord channel through the webhook integration the step names, and writes the
/// posted message's ids to <c>discord</c>.
/// </summary>
internal sealed class DiscordNodeExecutor(IIntegrationResolver resolver, DiscordChannel channel, ActionTemplates templates) : INodeExecutor
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
        var result = await channel.SendAsync(integration, await templates.FillAsync(ctx, config, ct), ct);
        if (!result.Ok)
        {
            return new NodeExecutionResult.Fail(result.Code!, result.Message!, result.Retryable, null);
        }

        await resolver.MarkUsedAsync(integration, ct);
        var patch = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(result.Status ?? 200)
        };
        if (result.Metadata is { } sent)
        {
            patch["discord"] = sent;
        }

        return new NodeExecutionResult.Continue("default", patch);
    }
}

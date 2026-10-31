using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Posts a message through the Telegram Bot API, as the bot of the Telegram integration the step names,
/// and writes the sent message's ids to <c>telegram</c>.
/// </summary>
/// <remarks>
/// The bot token lives in the integration, sealed, never in the node: a definition is stored,
/// versioned and readable by the whole workspace, so a token written into one leaks to every member
/// and is frozen into every published version.
/// </remarks>
internal sealed class TelegramNodeExecutor(IIntegrationResolver resolver, TelegramChannel channel, ActionTemplates templates) : INodeExecutor
{
    public string Kind => NodeKind.ActionTelegram;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (TelegramNotificationNode)ctx.Node;
        TelegramConfig config = node.Config
            ?? throw new InvalidOperationException($"Telegram node {node.NodeId} is missing config.");

        var resolution = await resolver.ResolveAsync(ctx.Branch, config.IntegrationRef, IntegrationType.Telegram, ct);
        if (resolution.Failure is not null)
        {
            return resolution.Failure;
        }

        var integration = resolution.Integration!;
        config = config with
        {
            ChatId = await templates.LineAsync(ctx, "chatId", config.ChatId, ct),
            Message = await templates.TextAsync(ctx, "message", config.Message, ct)
        };
        var result = await channel.SendAsync(integration, config, ct);
        if (!result.Ok)
        {
            // 4xx means the request itself is wrong - unknown chat, revoked token, a chat that blocked
            // the bot - and will be wrong again next time. 429 is the exception: Telegram rate-limits, and that is exactly what a
            // retry is for.
            return new NodeExecutionResult.Fail(result.Code!, result.Message!, result.Retryable, null);
        }

        await resolver.MarkUsedAsync(integration, ct);
        var patch = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement(result.Status ?? 200)
        };
        if (result.Metadata is { } sent)
        {
            patch["telegram"] = sent;
        }

        return new NodeExecutionResult.Continue("default", patch);
    }
}

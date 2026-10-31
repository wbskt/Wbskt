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
        var result = await channel.SendAsync(integration, await RenderAsync(ctx, config, ct), ct);
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

    private async Task<DiscordConfig> RenderAsync(NodeContext ctx, DiscordConfig config, CancellationToken ct)
    {
        List<DiscordEmbed>? embeds = null;
        for (int i = 0; i < (config.Embeds?.Count ?? 0); i++)
        {
            (embeds ??= []).Add(await RenderAsync(ctx, $"embeds[{i}]", config.Embeds![i], ct));
        }

        return config with
        {
            Message = await templates.TextOrNullAsync(ctx, "message", config.Message, ct),
            Username = await templates.LineOrNullAsync(ctx, "username", config.Username, ct),
            AvatarUrl = await templates.LineOrNullAsync(ctx, "avatarUrl", config.AvatarUrl, ct),
            ThreadId = await templates.LineOrNullAsync(ctx, "threadId", config.ThreadId, ct),
            Embeds = embeds
        };
    }

    private async Task<DiscordEmbed> RenderAsync(NodeContext ctx, string at, DiscordEmbed embed, CancellationToken ct)
    {
        List<DiscordEmbedField>? fields = null;
        for (int i = 0; i < (embed.Fields?.Count ?? 0); i++)
        {
            DiscordEmbedField field = embed.Fields![i];
            (fields ??= []).Add(field with
            {
                Name = await templates.TextAsync(ctx, $"{at}.fields[{i}].name", field.Name, ct),
                Value = await templates.TextAsync(ctx, $"{at}.fields[{i}].value", field.Value, ct)
            });
        }

        return embed with
        {
            Title = await templates.TextOrNullAsync(ctx, $"{at}.title", embed.Title, ct),
            Url = await templates.LineOrNullAsync(ctx, $"{at}.url", embed.Url, ct),
            Description = await templates.TextOrNullAsync(ctx, $"{at}.description", embed.Description, ct),
            Footer = await templates.TextOrNullAsync(ctx, $"{at}.footer", embed.Footer, ct),
            ImageUrl = await templates.LineOrNullAsync(ctx, $"{at}.imageUrl", embed.ImageUrl, ct),
            ThumbnailUrl = await templates.LineOrNullAsync(ctx, $"{at}.thumbnailUrl", embed.ThumbnailUrl, ct),
            Fields = fields
        };
    }
}

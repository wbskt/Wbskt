using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>
/// Posts a message to a Discord channel through the webhook integration the step names, and writes the
/// posted message's ids to <c>discord</c>.
/// </summary>
internal sealed class DiscordNodeExecutor(IIntegrationResolver resolver, DiscordChannel channel, ActionTemplates templates)
    : IntegrationStepExecutor<DiscordNotificationNode, DiscordConfig>(resolver, templates)
{
    public override string Kind => NodeKind.ActionDiscord;

    protected override IntegrationType IntegrationType => IntegrationType.Discord;

    protected override string? MetadataKey => "discord";

    protected override DiscordConfig? ConfigOf(DiscordNotificationNode node) => node.Config;

    protected override Guid? IntegrationRefOf(DiscordConfig config) => config.IntegrationRef;

    protected override Task<ChannelResult> SendAsync(ResolvedIntegration integration, DiscordConfig config, CancellationToken ct) =>
        channel.SendAsync(integration, config, ct);
}

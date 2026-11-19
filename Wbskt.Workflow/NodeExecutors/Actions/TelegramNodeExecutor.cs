using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
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
internal sealed class TelegramNodeExecutor(IIntegrationResolver resolver, TelegramChannel channel, ActionTemplates templates)
    : IntegrationStepExecutor<TelegramNotificationNode, TelegramConfig>(resolver, templates)
{
    public override string Kind => NodeKind.ActionTelegram;

    protected override IntegrationType IntegrationType => IntegrationType.Telegram;

    protected override string? MetadataKey => "telegram";

    protected override TelegramConfig? ConfigOf(TelegramNotificationNode node) => node.Config;

    protected override Guid? IntegrationRefOf(TelegramConfig config) => config.IntegrationRef;

    protected override Task<ChannelResult> SendAsync(ResolvedIntegration integration, TelegramConfig config, CancellationToken ct) =>
        channel.SendAsync(integration, config, ct);
}

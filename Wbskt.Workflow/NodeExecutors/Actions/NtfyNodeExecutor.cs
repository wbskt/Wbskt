using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>Publishes a push notification to the ntfy topic of the integration the step names.</summary>
internal sealed class NtfyNodeExecutor(IIntegrationResolver resolver, NtfyChannel channel, ActionTemplates templates)
    : IntegrationStepExecutor<NtfyNotificationNode, NtfyConfig>(resolver, templates)
{
    public override string Kind => NodeKind.ActionNtfy;

    protected override IntegrationType IntegrationType => IntegrationType.Ntfy;

    protected override NtfyConfig? ConfigOf(NtfyNotificationNode node) => node.Config;

    protected override Guid? IntegrationRefOf(NtfyConfig config) => config.IntegrationRef;

    protected override Task<ChannelResult> SendAsync(ResolvedIntegration integration, NtfyConfig config, CancellationToken ct) =>
        channel.PublishAsync(integration, config.Message, config.Title, config.Priority, config.Tags, ct);
}

using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>Sends a plain-text email through the SMTP integration the step names, and writes the recipient to <c>to</c>.</summary>
internal sealed class EmailNodeExecutor(IIntegrationResolver resolver, SmtpChannel channel, ActionTemplates templates)
    : IntegrationStepExecutor<EmailNotificationNode, EmailConfig>(resolver, templates)
{
    public override string Kind => NodeKind.ActionEmail;

    protected override IntegrationType IntegrationType => IntegrationType.Smtp;

    protected override EmailConfig? ConfigOf(EmailNotificationNode node) => node.Config;

    protected override Guid? IntegrationRefOf(EmailConfig config) => config.IntegrationRef;

    protected override NodeExecutionResult.Fail? Check(EmailConfig config) =>
        string.IsNullOrWhiteSpace(config.To)
            ? new NodeExecutionResult.Fail("EMAIL_RECIPIENT_MISSING", "The email node has no recipient.", false, null)
            : null;

    protected override Task<ChannelResult> SendAsync(ResolvedIntegration integration, EmailConfig config, CancellationToken ct) =>
        channel.SendAsync(integration, config.To, config.Subject, config.Body, ct);

    protected override void AddOutputs(EmailConfig config, Dictionary<string, JsonElement> output) =>
        output["to"] = JsonSerializer.SerializeToElement(config.To);
}

using System.Text.Json;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Integrations;

namespace Wbskt.Workflow.NodeExecutors.Actions;

/// <summary>Sends a plain-text email through the SMTP integration the step names.</summary>
internal sealed class EmailNodeExecutor(IIntegrationResolver resolver, SmtpChannel channel) : INodeExecutor
{
    public string Kind => NodeKind.ActionEmail;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (EmailNotificationNode)ctx.Node;
        EmailConfig config = node.Config
            ?? throw new InvalidOperationException($"Email node {node.NodeId} is missing config.");

        if (string.IsNullOrWhiteSpace(config.To))
        {
            return new NodeExecutionResult.Fail("EMAIL_RECIPIENT_MISSING", "The email node has no recipient.", false, null);
        }

        var resolution = await resolver.ResolveAsync(ctx.Branch, config.IntegrationRef, IntegrationType.Smtp, ct);
        if (resolution.Failure is not null)
        {
            return resolution.Failure;
        }

        var integration = resolution.Integration!;

        // A malformed address, a refused sign-in or a refused recipient fails the same way every time;
        // a relay that is down, greylisting or busy is worth retrying, and the retry policy decides how hard.
        var result = await channel.SendAsync(integration, config.To, config.Subject, config.Body, ct);
        if (!result.Ok)
        {
            return new NodeExecutionResult.Fail(result.Code!, result.Message!, result.Retryable, null);
        }

        await resolver.MarkUsedAsync(integration, ct);
        return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
        {
            ["to"] = JsonSerializer.SerializeToElement(config.To)
        });
    }
}

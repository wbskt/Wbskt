using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Actions;

internal sealed class EmailNodeExecutor(IEmailSender emailSender) : INodeExecutor
{
    public string Kind => NodeKind.ActionEmail;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (EmailNotificationNode)ctx.Node;
        EmailConfig config = node.Config
            ?? throw new InvalidOperationException($"Email node {node.NodeId} is missing config.");

        // Checked up front so an unconfigured host says so, rather than failing somewhere inside the
        // SMTP stack with a message about a null hostname. Not retryable - no number of attempts
        // configures a relay.
        if (!emailSender.IsConfigured)
        {
            return new NodeExecutionResult.Fail(
                "EMAIL_NOT_CONFIGURED",
                "This engine has no SMTP relay configured (WorkflowEngine:Email), so email nodes cannot send.",
                false,
                null);
        }

        if (string.IsNullOrWhiteSpace(config.To))
        {
            return new NodeExecutionResult.Fail("EMAIL_RECIPIENT_MISSING", "The email node has no recipient.", false, null);
        }

        try
        {
            await emailSender.SendAsync(config.To, config.Subject, config.Body, ct);
        }
        catch (FormatException ex)
        {
            // A malformed address fails the same way on every attempt.
            return new NodeExecutionResult.Fail("EMAIL_ADDRESS_INVALID", ex.Message, false, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Everything else - relay down, greylisted, connection reset - is worth retrying. SMTP is
            // routinely transient, and the retry policy is what decides how hard to try.
            return new NodeExecutionResult.Fail("EMAIL_SEND_ERROR", ex.Message, true, ex);
        }

        return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
        {
            ["to"] = JsonSerializer.SerializeToElement(config.To)
        });
    }
}

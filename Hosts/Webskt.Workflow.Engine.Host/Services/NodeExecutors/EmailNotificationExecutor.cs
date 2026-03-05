using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Actions;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class EmailNotificationExecutor : IWorkflowNodeExecutor
{
    private readonly ILogger<EmailNotificationExecutor> _logger;

    public EmailNotificationExecutor(ILogger<EmailNotificationExecutor> logger)
    {
        _logger = logger;
    }

    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var emailNode = (EmailNotificationNode)node;
        _logger.LogInformation("EMAIL SENT to {Recipient}: {Subject}", emailNode.Recipient, emailNode.Subject);
        return Task.FromResult(NodeExecutionResult.Success(PortNames.Out));
    }
}

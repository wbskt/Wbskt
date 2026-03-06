using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

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

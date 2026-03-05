using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Actions;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class ToastNotificationExecutor : IWorkflowNodeExecutor
{
    private readonly ILogger<ToastNotificationExecutor> _logger;

    public ToastNotificationExecutor(ILogger<ToastNotificationExecutor> logger)
    {
        _logger = logger;
    }

    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var toastNode = (ToastNotificationNode)node;

        _logger.LogInformation("TOAST NOTIFICATION: {Title} - {Message}", toastNode.Title, toastNode.Message);

        // Actions usually fire the 'out' port to continue the flow
        return Task.FromResult(NodeExecutionResult.Success(PortNames.Out));
    }
}

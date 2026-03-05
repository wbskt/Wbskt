using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Abstraction.Models.Nodes.Actions;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class TelegramNotificationExecutor : IWorkflowNodeExecutor
{
    private readonly ILogger<TelegramNotificationExecutor> _logger;

    public TelegramNotificationExecutor(ILogger<TelegramNotificationExecutor> logger)
    {
        _logger = logger;
    }

    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        var telegramNode = (TelegramNotificationNode)node;
        _logger.LogInformation("TELEGRAM MSG to {ChatId}: {Message}", telegramNode.ChatId, telegramNode.Message);
        return Task.FromResult(NodeExecutionResult.Success(PortNames.Out));
    }
}

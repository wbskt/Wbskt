using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

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

using Webskt.Workflow.Abstraction.Models;
using Webskt.Workflow.Abstraction.Models.Nodes;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Models;
using Webskt.Workflow.Engine.Host.Models.TriggerContexts;
using ExecutionContext = Webskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Webskt.Workflow.Engine.Host.Services.NodeExecutors;

public sealed class DeviceTriggerExecutor : IWorkflowNodeExecutor
{
    public Task<NodeExecutionResult> ExecuteAsync(BaseNode node, ExecutionContext context)
    {
        // 1. Extract data from the typed context
        if (context.TriggerContext is ClientPayloadTriggerContext payloadContext)
        {
            context.LastNodeOutput = payloadContext.Data;
        }
        else if (context.TriggerContext is ClientPropertyChangeTriggerContext propContext)
        {
            context.LastNodeOutput = propContext.NewValue;
        }

        // 2. Triggers always fire the 'out' port
        return Task.FromResult(NodeExecutionResult.Success(PortNames.Out));
    }
}

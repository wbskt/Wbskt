using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Models.TriggerContexts;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Services.NodeExecutors;

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

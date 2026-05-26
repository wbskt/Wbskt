using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

public sealed class VariableNodeExecutor : INodeExecutor
{
    private const int MaxCompareAndSetAttempts = 3;
    private readonly ISharedVariableProvider _sharedVariableProvider;

    public VariableNodeExecutor(ISharedVariableProvider sharedVariableProvider)
    {
        _sharedVariableProvider = sharedVariableProvider;
    }

    public string Kind => NodeKind.ControlVariable;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not VariableNode node || node.Config is null)
        {
            return new NodeExecutionResult.Fail("VARIABLE_CONFIG_INVALID", "Variable node config is required.", false, null);
        }

        if (node.Config.Op != VariableOperation.Set)
        {
            return new NodeExecutionResult.Fail("VARIABLE_OPERATION_NOT_SUPPORTED", $"Variable operation {node.Config.Op} is not implemented.", false, null);
        }

        JsonElement value = node.Config.Value?.Clone() ?? JsonSerializer.SerializeToElement((string?)null);
        if (node.Config.Scope == VariableScope.Local)
        {
            return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>
            {
                [node.Config.Var] = value.Clone()
            });
        }

        return await SetSharedAsync(ctx.Branch.WorkflowDefinitionRefId, node.Config.Var, value, ct);
    }

    private async Task<NodeExecutionResult> SetSharedAsync(Guid workflowRefId, string varName, JsonElement value, CancellationToken ct)
    {
        string newValueJson = JsonSerializer.Serialize(value);
        for (int attempt = 0; attempt < MaxCompareAndSetAttempts; attempt++)
        {
            SharedVariableRow current;
            try
            {
                current = await _sharedVariableProvider.GetByWorkflowRefIdNameAsync(workflowRefId, varName, ct);
            }
            catch (KeyNotFoundException)
            {
                await _sharedVariableProvider.InitializeAsync(workflowRefId, varName, "Json", newValueJson, ct);
                return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
            }

            int updated = await _sharedVariableProvider.CompareAndSetAsync(workflowRefId, varName, current.ValueJson, newValueJson, ct);
            if (updated > 0)
            {
                return new NodeExecutionResult.Continue("default", new Dictionary<string, JsonElement>());
            }
        }

        return new NodeExecutionResult.Fail("SHARED_VAR_CAS_FAILED", $"Failed to set shared variable '{varName}' after {MaxCompareAndSetAttempts} compare-and-set attempts.", true, null);
    }
}

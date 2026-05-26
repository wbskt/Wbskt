using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal static class RetryExecutor
{
    public static async Task<NodeExecutionResult> RunWithRetryAsync(
        BaseNode node,
        BranchContext context,
        INodeExecutor executor,
        INodeExecutionServices services,
        IClock clock,
        CancellationToken ct)
    {
        _ = clock;
        RetryPolicy policy = GetPolicy(node);

        for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            NodeContext nodeContext = new()
            {
                Branch = context with { Attempt = attempt },
                Node = node,
                Providers = services.Providers,
                Tick = services.Tick,
                ParentResults = services.ParentResults,
                CancellationToken = ct
            };

            NodeExecutionResult result = await executor.ExecuteAsync(nodeContext, ct);
            if (result is not NodeExecutionResult.Fail fail)
            {
                return result;
            }

            if (!fail.Retryable || attempt >= policy.MaxAttempts)
            {
                return fail;
            }

            TimeSpan delay = GetDelay(policy, attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }
        }

        return new NodeExecutionResult.Fail("RETRY_EXHAUSTED", "Retry policy exhausted without a terminal result.", false, null);
    }

    private static RetryPolicy GetPolicy(BaseNode node)
    {
        return node is BaseActionNode actionNode && actionNode.Retry is not null
            ? actionNode.Retry
            : new RetryPolicy(RetryStrategy.Constant, TimeSpan.Zero, null, null, 1, 0, []);
    }

    private static TimeSpan GetDelay(RetryPolicy policy, int attempt)
    {
        return policy.Strategy switch
        {
            RetryStrategy.Constant or RetryStrategy.None => policy.InitialDelay,
            RetryStrategy.Linear => TimeSpan.FromMilliseconds(policy.InitialDelay.TotalMilliseconds * attempt),
            RetryStrategy.Exponential => TimeSpan.FromMilliseconds(policy.InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)),
            _ => policy.InitialDelay
        };
    }
}

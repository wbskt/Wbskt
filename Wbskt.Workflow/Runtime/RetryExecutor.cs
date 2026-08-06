using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Exceptions;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Runtime;

internal static class RetryExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<NodeExecutionResult> RunWithRetryAsync(
        BaseNode node,
        BranchContext context,
        INodeExecutor executor,
        INodeExecutionServices services,
        IClock clock,
        CancellationToken ct,
        IRunCountersProvider? runCountersProvider = null,
        ICreditCostCalculator? creditCostCalculator = null,
        WorkflowMetrics? workflowMetrics = null,
        WorkflowDefinition? definition = null)
    {
        _ = clock;
        RetryPolicy policy = GetPolicy(node);
        bool isSideEffectFree = executor.IsSideEffectFree;

        IIdempotencyKeyProvider? idempotencyKeyProvider = null;
        if (!isSideEffectFree)
        {
            try
            {
                idempotencyKeyProvider = services.Providers.IdempotencyKey;
            }
            catch (NotSupportedException)
            {
                // Fallback for test stubs
            }
        }

        for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            string keyValue = $"action:{context.RunId}:{context.BranchRefId:N}:{node.NodeId:N}:{attempt}";
            IdempotencyKeyRow? existingRow = null;

            if (idempotencyKeyProvider != null)
            {
                try
                {
                    existingRow = await idempotencyKeyProvider.GetByKeyAsync(keyValue, ct);
                }
                catch (KeyNotFoundException)
                {
                    // Key does not exist yet; normal flow.
                }
                catch (NotSupportedException)
                {
                    idempotencyKeyProvider = null;
                }

                if (idempotencyKeyProvider != null && existingRow != null)
                {
                    if (string.Equals(existingRow.Status, "Succeeded", StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(existingRow.ResultJson))
                        {
                            var cachedResult = JsonSerializer.Deserialize<NodeExecutionResult>(existingRow.ResultJson, JsonOptions);
                            if (cachedResult != null)
                            {
                                return cachedResult;
                            }
                        }
                    }
                    else if (string.Equals(existingRow.Status, "Failed", StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(existingRow.ErrorJson))
                        {
                            var cachedFail = JsonSerializer.Deserialize<NodeExecutionResult.Fail>(existingRow.ErrorJson, JsonOptions);
                            if (cachedFail != null)
                            {
                                return cachedFail;
                            }
                        }
                    }
                    // If the status is "Pending", it means a prior run crashed mid-execution, so we must re-execute.
                }
            }

            Guid claimToken = Guid.NewGuid();
            if (!isSideEffectFree && idempotencyKeyProvider != null)
            {
                if (existingRow == null)
                {
                    try
                    {
                        existingRow = await idempotencyKeyProvider.UpsertPendingAsync(
                            keyValue, (int)context.RunId, claimToken, node.NodeId, attempt, ct);

                        if (existingRow.BranchRefId != claimToken)
                        {
                            if (string.Equals(existingRow.Status, "Succeeded", StringComparison.Ordinal))
                            {
                                if (!string.IsNullOrWhiteSpace(existingRow.ResultJson))
                                {
                                    var cachedResult = JsonSerializer.Deserialize<NodeExecutionResult>(existingRow.ResultJson, JsonOptions);
                                    if (cachedResult != null)
                                    {
                                        return cachedResult;
                                    }
                                }
                            }
                            else if (string.Equals(existingRow.Status, "Failed", StringComparison.Ordinal))
                            {
                                if (!string.IsNullOrWhiteSpace(existingRow.ErrorJson))
                                {
                                    var cachedFail = JsonSerializer.Deserialize<NodeExecutionResult.Fail>(existingRow.ErrorJson, JsonOptions);
                                    if (cachedFail != null)
                                    {
                                        return cachedFail;
                                    }
                                }
                            }
                            // If it was Pending, we continue to run the executor.
                        }
                    }
                    catch (NotSupportedException)
                    {
                        idempotencyKeyProvider = null;
                    }
                }
            }

            if (runCountersProvider != null)
            {
                var calc = creditCostCalculator ?? new DefaultCreditCostCalculator();
                decimal cost = calc.Calculate(node, null!);

                // Atomic budget-checked charge (2.2): the SP re-validates the budget under the
                // same row lock as the increment, so this is a single round-trip with no
                // check-then-act race between concurrent branches on the same run.
                bool charged = await runCountersProvider.TryChargeAsync((int)context.RunId, cost, ct);
                if (!charged)
                {
                    var outOfCreditsResult = new NodeExecutionResult.Fail("OUT_OF_CREDITS", "Credit budget exhausted.", false, null);
                    string errorJson = JsonSerializer.Serialize(outOfCreditsResult, JsonOptions);

                    if (idempotencyKeyProvider != null)
                    {
                        try
                        {
                            if (!isSideEffectFree)
                            {
                                await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                            }
                            else
                            {
                                await idempotencyKeyProvider.UpsertPendingAsync(
                                    keyValue, (int)context.RunId, Guid.NewGuid(), node.NodeId, attempt, ct);
                                await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                            }
                        }
                        catch (NotSupportedException)
                        {
                            idempotencyKeyProvider = null;
                        }
                    }

                    return outOfCreditsResult;
                }

                if (workflowMetrics != null)
                {
                    workflowMetrics.RecordCreditsConsumed(context.WorkflowDefinitionRefId.ToString(), (double)cost);
                }
            }

            NodeContext nodeContext = new()
            {
                Branch = context with { Attempt = attempt },
                Node = node,
                Definition = definition,
                Providers = services.Providers,
                Tick = services.Tick,
                ParentResults = services.ParentResults,
                CancellationToken = ct,
                IdempotencyKey = keyValue
            };

            try
            {
                NodeExecutionResult result = await executor.ExecuteAsync(nodeContext, ct);

                if (result is NodeExecutionResult.Fail fail)
                {
                    string errorJson = JsonSerializer.Serialize(fail, JsonOptions);
                    if (idempotencyKeyProvider != null)
                    {
                        try
                        {
                            if (!isSideEffectFree)
                            {
                                await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                            }
                            else
                            {
                                await idempotencyKeyProvider.UpsertPendingAsync(
                                    keyValue, (int)context.RunId, Guid.NewGuid(), node.NodeId, attempt, ct);
                                await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                            }
                        }
                        catch (NotSupportedException)
                        {
                            idempotencyKeyProvider = null;
                        }
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
                    continue;
                }

                if (result is NodeExecutionResult.WaitForBookmark)
                {
                    return result;
                }

                string resultJson = JsonSerializer.Serialize(result, JsonOptions);
                if (idempotencyKeyProvider != null)
                {
                    try
                    {
                        if (!isSideEffectFree)
                        {
                            await idempotencyKeyProvider.MarkSucceededAsync(keyValue, resultJson, ct);
                        }
                        else
                        {
                            await idempotencyKeyProvider.UpsertPendingAsync(
                                keyValue, (int)context.RunId, Guid.NewGuid(), node.NodeId, attempt, ct);
                            await idempotencyKeyProvider.MarkSucceededAsync(keyValue, resultJson, ct);
                        }
                    }
                    catch (NotSupportedException)
                    {
                        idempotencyKeyProvider = null;
                    }
                }

                return result;
            }
            catch (TransientNodeException ex)
            {
                var fail = new NodeExecutionResult.Fail("TRANSIENT_ERROR", ex.Message, true, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                if (idempotencyKeyProvider != null)
                {
                    try
                    {
                        if (!isSideEffectFree)
                        {
                            await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                        }
                        else
                        {
                            await idempotencyKeyProvider.UpsertPendingAsync(
                                keyValue, (int)context.RunId, Guid.NewGuid(), node.NodeId, attempt, ct);
                            await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                        }
                    }
                    catch (NotSupportedException)
                    {
                        idempotencyKeyProvider = null;
                    }
                }

                if (attempt >= policy.MaxAttempts)
                {
                    return fail;
                }

                TimeSpan delay = GetDelay(policy, attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
            }
            catch (PermanentNodeException ex)
            {
                var fail = new NodeExecutionResult.Fail(ex.ErrorCode, ex.Message, false, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                if (idempotencyKeyProvider != null)
                {
                    try
                    {
                        if (!isSideEffectFree)
                        {
                            await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                        }
                        else
                        {
                            await idempotencyKeyProvider.UpsertPendingAsync(
                                keyValue, (int)context.RunId, Guid.NewGuid(), node.NodeId, attempt, ct);
                            await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                        }
                    }
                    catch (NotSupportedException)
                    {
                        idempotencyKeyProvider = null;
                    }
                }

                return fail;
            }
            catch (EngineFaultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var fail = new NodeExecutionResult.Fail("EXECUTOR_CRASH", ex.Message, false, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                if (idempotencyKeyProvider != null)
                {
                    try
                    {
                        if (!isSideEffectFree)
                        {
                            await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                        }
                        else
                        {
                            await idempotencyKeyProvider.UpsertPendingAsync(
                                keyValue, (int)context.RunId, Guid.NewGuid(), node.NodeId, attempt, ct);
                            await idempotencyKeyProvider.MarkFailedAsync(keyValue, errorJson, ct);
                        }
                    }
                    catch (NotSupportedException)
                    {
                        idempotencyKeyProvider = null;
                    }
                }

                return fail;
            }
        }

        return new NodeExecutionResult.Fail("RETRY_EXHAUSTED", "Retry policy exhausted without a terminal result.", false, null);
    }

    private static RetryPolicy GetPolicy(BaseNode node)
    {
        if (node is BaseActionNode actionNode && actionNode.Retry is not null)
        {
            return actionNode.Retry;
        }
        return new RetryPolicy { Strategy = RetryStrategy.Constant, InitialDelay = TimeSpan.Zero, Factor = null, MaxDelay = null, MaxAttempts = 1, JitterPct = 0, RetryOn = [] };
    }

    private static TimeSpan GetDelay(RetryPolicy policy, int attempt)
    {
        double baseMs = policy.Strategy switch
        {
            RetryStrategy.Constant or RetryStrategy.None => policy.InitialDelay.TotalMilliseconds,
            RetryStrategy.Linear => policy.InitialDelay.TotalMilliseconds * attempt,
            RetryStrategy.Exponential => policy.InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1),
            _ => policy.InitialDelay.TotalMilliseconds
        };

        if (policy.JitterPct > 0)
        {
            var random = new Random();
            double pct = random.Next(-policy.JitterPct, policy.JitterPct + 1) / 100.0;
            baseMs += baseMs * pct;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, baseMs));
    }
}

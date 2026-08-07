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

/// <summary>
/// Called when a transient failure is about to be retried, before the backoff delay elapses. The
/// retry executor has no history provider of its own, so the branch loop supplies this to record the
/// attempt.
/// </summary>
internal delegate Task RetryNotification(int attempt, int maxAttempts, TimeSpan delay, string reason, CancellationToken ct);

/// <summary>
/// Called each time an attempt is charged against the run's credit budget. Reported back rather than
/// recorded here for the same reason as <see cref="RetryNotification"/> - the retry executor has no
/// history provider - and the branch loop folds the total onto the node's outcome event rather than
/// writing a row per attempt, since history is written synchronously.
/// </summary>
internal delegate void CreditChargeNotification(decimal cost);

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
        WorkflowDefinition? definition = null,
        RetryNotification? onRetry = null,
        CreditChargeNotification? onCharge = null)
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

            // Recording an attempt's outcome was written out six times, identically. The duplication hid
            // two things worth stating once: a provider that does not support this is disabled for the
            // rest of the call rather than retried into, and there is no side-effect-free path here at
            // all - a side-effect-free node never claims a key, so idempotencyKeyProvider is null for
            // one and every call below is a no-op. (The old code had an isSideEffectFree branch inside
            // each block that could therefore never run, and which wrote two rows nothing ever read.)
            async Task RecordOutcomeAsync(bool succeeded, string json)
            {
                if (idempotencyKeyProvider is null)
                {
                    return;
                }

                try
                {
                    if (succeeded)
                    {
                        await idempotencyKeyProvider.MarkSucceededAsync(keyValue, json, ct);
                    }
                    else
                    {
                        await idempotencyKeyProvider.MarkFailedAsync(keyValue, json, ct);
                    }
                }
                catch (NotSupportedException)
                {
                    idempotencyKeyProvider = null;
                }
            }

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

                    await RecordOutcomeAsync(false, errorJson);

                    return outOfCreditsResult;
                }

                onCharge?.Invoke(cost);

                if (workflowMetrics != null)
                {
                    workflowMetrics.RecordCreditsConsumed(context.WorkflowDefinitionRefId.ToString(), (double)cost, context.WorkspaceId);
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
                    await RecordOutcomeAsync(false, errorJson);

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
                await RecordOutcomeAsync(true, resultJson);

                return result;
            }
            catch (TransientNodeException ex)
            {
                var fail = new NodeExecutionResult.Fail("TRANSIENT_ERROR", ex.Message, true, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                await RecordOutcomeAsync(false, errorJson);

                // policy.RetryOn was collected but never consulted, so a policy saying "only retry
                // these" retried everything transient. An empty list still means "retry anything".
                if (!ShouldRetry(policy, fail.ErrorCode, ex))
                {
                    return fail with { Retryable = false };
                }

                if (attempt >= policy.MaxAttempts)
                {
                    return fail;
                }

                TimeSpan delay = GetDelay(policy, attempt);

                // Retries were invisible in the trace: a node retried four times looked like one slow
                // node, and the backoff was indistinguishable from the node itself being slow.
                if (onRetry is not null)
                {
                    await onRetry(attempt, policy.MaxAttempts, delay, ex.Message, ct);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
            }
            catch (PermanentNodeException ex)
            {
                var fail = new NodeExecutionResult.Fail(ex.ErrorCode, ex.Message, false, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                await RecordOutcomeAsync(false, errorJson);

                return fail;
            }
            catch (EngineFaultException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // A genuine cancellation - a run cancel or host shutdown - is not a crash and must not
                // be retried. BranchLoop is the only place that can tell those two apart (it holds both
                // tokens), and it turns the first into a clean Cancelled branch and leaves the second
                // Active for recovery. Swallowing this into EXECUTOR_CRASH here would have turned every
                // interrupted node into a failed one the moment run tokens started actually firing.
                throw;
            }
            catch (Exception ex)
            {
                var fail = new NodeExecutionResult.Fail("EXECUTOR_CRASH", ex.Message, false, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                await RecordOutcomeAsync(false, errorJson);

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

    /// <summary>
    /// Whether a transient failure is one this policy opted into retrying. An empty
    /// <see cref="RetryPolicy.RetryOn"/> means "anything transient"; otherwise the failure must match
    /// by error code or by exception type name, so a policy can retry a timeout without also
    /// retrying, say, a validation error the callee reported as transient.
    /// </summary>
    private static bool ShouldRetry(RetryPolicy policy, string errorCode, Exception exception)
    {
        if (policy.RetryOn.Count == 0)
        {
            return true;
        }

        return policy.RetryOn.Any(entry =>
            string.Equals(entry, errorCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry, exception.GetType().Name, StringComparison.OrdinalIgnoreCase));
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
            // Random.Shared, not a fresh Random per call: instances created in quick succession used
            // to share a seed, so concurrent retries jittered identically - defeating the point of
            // jitter, which is to stop simultaneous retries from re-colliding.
            double pct = Random.Shared.Next(-policy.JitterPct, policy.JitterPct + 1) / 100.0;
            baseMs += baseMs * pct;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, baseMs));
    }
}

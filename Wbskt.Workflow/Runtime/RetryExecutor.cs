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
        TimeProvider clock,
        CancellationToken ct,
        IRunCountersProvider runCountersProvider,
        ICreditCostCalculator creditCostCalculator,
        WorkflowMetrics? workflowMetrics = null,
        WorkflowDefinition? definition = null,
        RetryNotification? onRetry = null,
        CreditChargeNotification? onCharge = null)
    {
        RetryPolicy policy = GetPolicy(node);
        bool isReplaySafe = executor.IsReplaySafe(node);

        // A replay-safe node is simply run again on a retry or a redelivery; only a node whose replay
        // would repeat an effect outside the branch row is deduplicated.
        IIdempotencyKeyProvider? idempotencyKeyProvider = isReplaySafe ? null : services.IdempotencyKeys;

        for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            // The visit token is what lets a node run again when a loop brings the branch back to it.
            // Without it, every lap of a sequential ForEach shares the first lap's key, so laps two
            // onwards return the cached result and their side effect never happens.
            string keyValue = $"action:{context.RunId}:{context.BranchRefId:N}:{node.NodeId:N}:{context.VisitToken}:{attempt}";
            IdempotencyKeyRow? existingRow = null;

            // Recording an attempt's outcome, once. There is no replay-safe path here: a replay-safe
            // node never claims a key, so idempotencyKeyProvider is null for one and this is a no-op.
            async Task RecordOutcomeAsync(bool succeeded, string json)
            {
                if (idempotencyKeyProvider is null)
                {
                    return;
                }

                if (succeeded)
                {
                    await idempotencyKeyProvider.MarkSucceededAsync(keyValue, json, ct);
                }
                else
                {
                    await idempotencyKeyProvider.MarkFailedAsync(keyValue, json, ct);
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

                if (existingRow != null)
                {
                    if (string.Equals(existingRow.Status, IdempotencyKeyStatus.Succeeded, StringComparison.Ordinal))
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
                    else if (string.Equals(existingRow.Status, IdempotencyKeyStatus.Failed, StringComparison.Ordinal))
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
            if (!isReplaySafe && idempotencyKeyProvider != null)
            {
                if (existingRow == null)
                {
                    existingRow = await idempotencyKeyProvider.UpsertPendingAsync(
                        keyValue, (int)context.RunId, claimToken, node.NodeId, attempt, ct);

                    if (existingRow.BranchRefId != claimToken)
                    {
                        if (string.Equals(existingRow.Status, IdempotencyKeyStatus.Succeeded, StringComparison.Ordinal))
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
                        else if (string.Equals(existingRow.Status, IdempotencyKeyStatus.Failed, StringComparison.Ordinal))
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
            }

            decimal cost = creditCostCalculator.Calculate(node, null!);

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

            NodeContext nodeContext = new()
            {
                Branch = context with { Attempt = attempt },
                Node = node,
                Definition = definition,
                Tick = services.Tick,
                ParentResults = services.ParentResults,
                CancellationToken = ct,
                IdempotencyKey = keyValue
            };

            // Both ways a node can ask for a retry - returning a retryable Fail and throwing a
            // TransientNodeException - go through here. They used to be separate: the Fail path
            // ignored RetryOn and never reported the retry, so a policy saying "only retry these"
            // did not hold for it and its retries were missing from history. Null means "retry";
            // otherwise it is the failure to return.
            async Task<NodeExecutionResult.Fail?> RetryOrStopAsync(NodeExecutionResult.Fail fail, Exception? exception)
            {
                RetryDecision decision = Decide(policy, attempt, fail.ErrorCode, exception);
                switch (decision.Verdict)
                {
                    case RetryVerdict.NotRetryable:
                        return fail with { Retryable = false };
                    case RetryVerdict.Exhausted:
                        return fail;
                }

                // Retries were invisible in the trace: a node retried four times looked like one slow
                // node, and the backoff was indistinguishable from the node itself being slow.
                if (onRetry is not null)
                {
                    await onRetry(attempt, policy.MaxAttempts, decision.Delay, fail.Message, ct);
                }

                // On the injected clock, not the wall clock, so backoff follows the same time source as
                // the rest of the engine and can be driven by a test.
                if (decision.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(decision.Delay, clock, ct);
                }

                return null;
            }

            try
            {
                NodeExecutionResult result = await executor.ExecuteAsync(nodeContext, ct);

                if (result is NodeExecutionResult.Fail fail)
                {
                    string errorJson = JsonSerializer.Serialize(fail, JsonOptions);
                    await RecordOutcomeAsync(false, errorJson);

                    if (!fail.Retryable)
                    {
                        return fail;
                    }

                    if (await RetryOrStopAsync(fail, fail.Cause) is { } stop)
                    {
                        return stop;
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

                if (await RetryOrStopAsync(fail, ex) is { } stop)
                {
                    return stop;
                }
            }
            catch (PermanentNodeException ex)
            {
                var fail = new NodeExecutionResult.Fail(ex.ErrorCode, ex.Message, false, ex);
                string errorJson = JsonSerializer.Serialize(fail, JsonOptions);

                await RecordOutcomeAsync(false, errorJson);

                return fail;
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

    private enum RetryVerdict
    {
        Retry,
        NotRetryable,
        Exhausted
    }

    private readonly record struct RetryDecision(RetryVerdict Verdict, TimeSpan Delay);

    /// <summary>
    /// The one place that decides whether a retryable failure is tried again, and after how long.
    /// <see cref="RetryVerdict.NotRetryable"/> means the policy did not opt into this failure;
    /// <see cref="RetryVerdict.Exhausted"/> means it did but the attempts are used up.
    /// </summary>
    private static RetryDecision Decide(RetryPolicy policy, int attempt, string errorCode, Exception? exception)
    {
        // policy.RetryOn was once collected but never consulted, so a policy saying "only retry
        // these" retried everything transient. An empty list still means "retry anything".
        if (!ShouldRetry(policy, errorCode, exception))
        {
            return new RetryDecision(RetryVerdict.NotRetryable, TimeSpan.Zero);
        }

        if (attempt >= policy.MaxAttempts)
        {
            return new RetryDecision(RetryVerdict.Exhausted, TimeSpan.Zero);
        }

        return new RetryDecision(RetryVerdict.Retry, GetDelay(policy, attempt));
    }

    /// <summary>
    /// Whether a transient failure is one this policy opted into retrying. An empty
    /// <see cref="RetryPolicy.RetryOn"/> means "anything transient"; otherwise the failure must match
    /// by error code or by exception type name, so a policy can retry a timeout without also
    /// retrying, say, a validation error the callee reported as transient.
    /// </summary>
    private static bool ShouldRetry(RetryPolicy policy, string errorCode, Exception? exception)
    {
        if (policy.RetryOn.Count == 0)
        {
            return true;
        }

        return policy.RetryOn.Any(entry =>
            string.Equals(entry, errorCode, StringComparison.OrdinalIgnoreCase)
            || (exception is not null && string.Equals(entry, exception.GetType().Name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The exponential base when a policy names none. Also used for a stored definition whose factor
    /// is not a positive number: the validator now rejects one, but a definition published before
    /// that rule would otherwise collapse every backoff to zero (or fail to build a delay at all).
    /// </summary>
    private const double DefaultFactor = 2.0;

    /// <summary>
    /// The longest delay Task.Delay accepts. A high factor over many attempts can exceed it - or
    /// overflow to infinity - and a backoff that long is indistinguishable from "never" anyway.
    /// </summary>
    private const double LongestDelayMs = uint.MaxValue - 1.0;

    private static TimeSpan GetDelay(RetryPolicy policy, int attempt)
    {
        double factor = policy.Factor is { } f && double.IsFinite(f) && f > 0 ? f : DefaultFactor;

        double baseMs = policy.Strategy switch
        {
            RetryStrategy.Constant or RetryStrategy.None => policy.InitialDelay.TotalMilliseconds,
            RetryStrategy.Linear => policy.InitialDelay.TotalMilliseconds * attempt,
            // The growth is clamped before it is multiplied so the product stays finite; an
            // infinite one times a zero initial delay is NaN, which no TimeSpan can hold.
            RetryStrategy.Exponential => policy.InitialDelay.TotalMilliseconds * Math.Min(Math.Pow(factor, attempt - 1), LongestDelayMs),
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

        // Capped after jitter, so MaxDelay is a true ceiling rather than one jitter can overshoot.
        if (policy.MaxDelay is { } maxDelay)
        {
            baseMs = Math.Min(baseMs, maxDelay.TotalMilliseconds);
        }

        baseMs = Math.Min(baseMs, LongestDelayMs);

        return TimeSpan.FromMilliseconds(Math.Max(0, baseMs));
    }
}

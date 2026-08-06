namespace Wbskt.Workflow.Abstraction.Providers;

public interface IJoinAggregatorProvider
{
    /// <summary>
    /// Opens a cohort. Mode/quorum/join-node are stamped here rather than supplied per contribution
    /// because a FAILED branch contributes from the branch loop, which has no view of the Join
    /// node's config.
    /// </summary>
    Task InitializeAsync(
        Guid joinToken,
        int runId,
        int expectedCount,
        string mode,
        int quorumCount,
        Guid? joinNodeId,
        CancellationToken ct);

    /// <summary>
    /// Records one arrival. <paramref name="outcome"/> is "succeeded" or "failed".
    /// Exactly one caller ever receives <see cref="JoinContributionResult.ShouldContinue"/> = true
    /// for a given cohort (claimed under the same transaction that counts the arrival).
    /// </summary>
    Task<JoinContributionResult> ContributeAsync(Guid joinToken, string outcome, CancellationToken ct);

    Task DeleteAllByRunIdAsync(int runId, CancellationToken ct);
}

public sealed record JoinContributionResult(
    bool ShouldContinue,
    int ContributedCount,
    int SucceededCount,
    int FailedCount,
    int ExpectedCount,
    Guid? JoinNodeId = null);

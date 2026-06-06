namespace Wbskt.Workflow.Abstraction.Providers;

public interface IJoinAggregatorProvider
{
    Task InitializeAsync(Guid joinToken, int runId, int expectedCount, CancellationToken ct);
    Task<JoinContributionResult> ContributeAsync(Guid joinToken, string outcome, string mode, int quorumCount, CancellationToken ct);
}

public sealed record JoinContributionResult(
    bool ShouldContinue,
    int ContributedCount,
    int SucceededCount,
    int FailedCount,
    int ExpectedCount);

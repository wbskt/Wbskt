using Wbskt.Models.Admin;

namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// Runs a change a staff member asked for through the admin host, with its staff audit row: written in
/// the change's own transaction when it succeeds, on its own when it is refused, and once per idempotency
/// key however often the call is retried.
/// </summary>
public interface IStaffActionRunner
{
    /// <param name="change">
    /// The change itself, normally one existing service method. It must open its connections one after
    /// another, never two at once, so the transaction stays on one connection (see the remarks on
    /// <see cref="StaffActionRunner"/>).
    /// </param>
    /// <param name="afterCommit">
    /// What follows the change once it has committed, in order, and only for a change made now, not for a
    /// replay, which did it the first time. The change is made and audited by then, so each step runs on
    /// <see cref="CancellationToken.None"/> and a step that throws is logged, not reported as the change
    /// failing; the steps after it still run.
    /// </param>
    Task<Result<StaffActionResponse>> RunAsync(
        StaffChange staff,
        StaffAction action,
        Func<CancellationToken, Task<Result>> change,
        IReadOnlyList<AfterCommit> afterCommit,
        CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> RunAsync(
        StaffChange staff,
        StaffAction action,
        Func<CancellationToken, Task<Result>> change,
        CancellationToken cancellationToken) =>
        RunAsync(staff, action, change, [], cancellationToken);
}

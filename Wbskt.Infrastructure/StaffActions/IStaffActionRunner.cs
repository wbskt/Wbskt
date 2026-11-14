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
    Task<Result<StaffActionResponse>> RunAsync(
        StaffChange staff,
        StaffAction action,
        Func<CancellationToken, Task<Result>> change,
        CancellationToken cancellationToken);
}

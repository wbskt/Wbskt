using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;

namespace Wbskt.Host.Tests;

/// <summary>
/// Runs the change with no transaction or audit row and records what it was asked, for tests of the
/// services built on <see cref="IStaffActionRunner"/>. What follows the commit runs as the real runner runs
/// it: only for a change made now, each step on its own, a step that throws recorded rather than thrown.
/// </summary>
internal sealed class RecordingStaffActionRunner : IStaffActionRunner
{
    public StaffChange? Staff { get; private set; }

    public StaffAction? Action { get; private set; }

    /// <summary>A replay answers from the first attempt without running the change.</summary>
    public bool Replay { get; set; }

    /// <summary>Runs after the change, while its transaction would still be open.</summary>
    public System.Action? Inspect { get; set; }

    public List<string> FailedAfterCommit { get; } = [];

    public async Task<Result<StaffActionResponse>> RunAsync(
        IStaffRequest request,
        StaffAction action,
        Func<CancellationToken, Task<Result>> change,
        IReadOnlyList<AfterCommit> afterCommit,
        CancellationToken cancellationToken)
    {
        Staff = request.Staff;
        Action = action;
        if (Replay)
        {
            return new StaffActionResponse(1, true);
        }

        var outcome = await change(cancellationToken);
        Inspect?.Invoke();
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        foreach (var step in afterCommit)
        {
            try
            {
                await step.Effect(CancellationToken.None);
            }
            catch (Exception)
            {
                FailedAfterCommit.Add(step.What);
            }
        }

        return new StaffActionResponse(1, false);
    }
}

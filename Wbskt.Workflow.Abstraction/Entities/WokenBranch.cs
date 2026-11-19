namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>
/// A Waiting branch whose wake a claim stored (<c>dbo.Branch_GetWoken</c>): enough to dispatch it, and
/// the row version that tells whether it was written since it was last dispatched.
/// </summary>
public sealed record WokenBranch(long Id, int RunId, byte[] RowVersion);

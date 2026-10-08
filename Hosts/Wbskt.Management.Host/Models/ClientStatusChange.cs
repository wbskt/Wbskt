namespace Wbskt.Management.Host.Models;

/// <summary>What dbo.Client_UpdateStatuses did with one client of the list it was given.</summary>
public enum ClientStatusOutcome : byte
{
    Updated = 0,
    AlreadyAtStatus = 1,
    NotFound = 2,
    OtherWorkspace = 3,
    PolicyLimitReached = 4
}

/// <summary>
/// One client's result from a status change. <see cref="Id"/>, the policy and
/// <see cref="OldStatus"/> are only meaningful when the client was found.
/// </summary>
public sealed record ClientStatusChange(
    Guid RefId,
    int Id,
    int PolicyId,
    Guid PolicyRefId,
    ClientStatus OldStatus,
    ClientStatusOutcome Outcome);

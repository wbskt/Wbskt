namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// Something a staff change does once it has committed, such as announcing it or revoking tokens, named
/// by <paramref name="What"/> for the log line when it fails. See <see cref="IStaffActionRunner"/>.
/// </summary>
public sealed record AfterCommit(string What, Func<CancellationToken, Task> Effect);

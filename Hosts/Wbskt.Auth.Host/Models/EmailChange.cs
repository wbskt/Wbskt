namespace Wbskt.Auth.Host.Models;

/// <summary>
/// An account's address moving from <see cref="From"/> to <see cref="To"/>, by a confirm or a revert
/// link, with the account's reference and name so the caller need not read it back.
/// </summary>
public sealed record EmailChange(int UserId, Guid UserRefId, string Username, string From, string To);

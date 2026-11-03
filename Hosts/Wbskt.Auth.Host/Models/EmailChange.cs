namespace Wbskt.Auth.Host.Models;

/// <summary>An account's address moving from <see cref="From"/> to <see cref="To"/>, by a confirm or a revert link.</summary>
public sealed record EmailChange(int UserId, string From, string To);

namespace Wbskt.Auth.Host.Models;

/// <summary>An account's name before and after a rename; the same when nothing changed.</summary>
public sealed record UsernameChange(string From, string To);

namespace Wbskt.Management.Host.Models;

/// <summary>What the main database holds of a credit grant: whether it was revoked, and the newest hold revision it applied.</summary>
public sealed record CreditGrantState(string Source, string SourceRef, bool Revoked, int HoldRevision);

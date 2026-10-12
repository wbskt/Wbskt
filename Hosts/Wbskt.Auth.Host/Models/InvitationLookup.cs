namespace Wbskt.Auth.Host.Models;

/// <summary>
/// An invitation as read back before it is redeemed. Not a response type — registration uses it to
/// reject a bad token before creating an account, rather than after.
/// </summary>
public record InvitationLookup(Guid RefId, Guid TenantRef, string TenantName, string Email, DateTime ExpiresAt, bool IsLive);

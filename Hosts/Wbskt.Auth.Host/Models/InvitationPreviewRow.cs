namespace Wbskt.Auth.Host.Models;

/// <summary>An invitation as the signed-out invite page reads it (dbo.TenantInvitation_GetPreview_By_TokenHash).</summary>
public sealed record InvitationPreviewRow(
    Guid RefId,
    string TenantName,
    string? InvitedBy,
    string Email,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? RevokedAt);

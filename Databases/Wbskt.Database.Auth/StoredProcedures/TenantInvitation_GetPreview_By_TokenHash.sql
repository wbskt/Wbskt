-- What the invite page shows before anyone signs in: the account, who invited, the address it was sent
-- to, and whether it is still pending. Live or not; the caller says which state it is in. No row for a
-- token that matches nothing.
CREATE PROCEDURE dbo.TenantInvitation_GetPreview_By_TokenHash
    @TokenHash VARBINARY(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT I.RefId,
           T.Name AS TenantName,
           U.Username AS InvitedBy,
           I.Email,
           I.ExpiresAt,
           I.AcceptedAt,
           I.RevokedAt
    FROM dbo.TenantInvitations I
    INNER JOIN dbo.Tenants T ON T.Id = I.TenantId
    LEFT JOIN dbo.Users U ON U.Id = I.InvitedByUserId
    WHERE I.TokenHash = @TokenHash;
END
GO

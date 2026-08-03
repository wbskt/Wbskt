-- Looks up an invitation by the hash of its token, live or not. The caller decides what to do with
-- a spent one; TenantInvitation_Accept is the procedure that enforces validity, and this exists so
-- that registration can reject a bad token before creating an account rather than after.
CREATE PROCEDURE dbo.TenantInvitation_GetBy_TokenHash
    @TokenHash VARBINARY(32)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT I.RefId,
           T.RefId AS TenantRefId,
           T.Name AS TenantName,
           I.Email,
           I.ExpiresAt,
           CAST(CASE WHEN I.AcceptedAt IS NULL
                      AND I.RevokedAt IS NULL
                      AND I.ExpiresAt > SYSUTCDATETIME()
                     THEN 1 ELSE 0 END AS BIT) AS IsLive
    FROM dbo.TenantInvitations I
    INNER JOIN dbo.Tenants T ON T.Id = I.TenantId
    WHERE I.TokenHash = @TokenHash;
END
GO

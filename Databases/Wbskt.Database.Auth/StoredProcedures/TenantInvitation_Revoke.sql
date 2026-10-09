-- Withdraws an outstanding invitation. Marked rather than deleted, so that "this invitation was
-- issued and then withdrawn" stays answerable; the filtered index treats a revoked row as gone and
-- allows the same address to be invited again. Returns the address when it revoked something, and
-- no row when the invitation was not outstanding.
CREATE PROCEDURE dbo.TenantInvitation_Revoke
    @RefId UNIQUEIDENTIFIER,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TenantInvitations
    SET RevokedAt = SYSUTCDATETIME()
    OUTPUT inserted.Email
    WHERE RefId = @RefId
      AND TenantId = @TenantId
      AND AcceptedAt IS NULL
      AND RevokedAt IS NULL;
END
GO

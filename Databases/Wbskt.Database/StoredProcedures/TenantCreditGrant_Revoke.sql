-- Revokes the grant made for (@Source, @SourceRef), as a refund does: its undrawn credits stop counting
-- from now. Returns 1 when it revoked, 0 when there was no such grant or it was already revoked.
CREATE PROCEDURE dbo.TenantCreditGrant_Revoke
    @Source    NVARCHAR(40),
    @SourceRef NVARCHAR(200),
    @RevokedAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TenantCreditGrants
    SET RevokedAt = @RevokedAt
    WHERE Source = @Source
      AND SourceRef = @SourceRef
      AND RevokedAt IS NULL;

    SELECT @@ROWCOUNT;
END
GO

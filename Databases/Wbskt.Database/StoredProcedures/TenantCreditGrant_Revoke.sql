-- Revokes the grant made for (@Source, @SourceRef), as a refund does: its undrawn credits stop counting
-- from now. Returns 1 when it revoked, 0 when there was no such grant or it was already revoked.
CREATE PROCEDURE dbo.TenantCreditGrant_Revoke
    @Source    NVARCHAR(40),
    @SourceRef NVARCHAR(200),
    @RevokedAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Lock order as dbo.TenantCreditGrants_Draw: the grant's IX_TenantCreditGrants_TenantId key, then
    -- its row. Updating by the unique key alone took the row first and then the index key the change
    -- has to rewrite, the reverse of a draw holding that key, and the two could deadlock.
    DECLARE @Id INT, @TenantId INT;
    SELECT @Id = Id, @TenantId = TenantId
    FROM dbo.TenantCreditGrants
    WHERE Source = @Source
      AND SourceRef = @SourceRef;

    BEGIN TRANSACTION;

    SELECT @Id = Id
    FROM dbo.TenantCreditGrants WITH (UPDLOCK, HOLDLOCK, INDEX (IX_TenantCreditGrants_TenantId))
    WHERE TenantId = @TenantId
      AND Id = @Id;

    UPDATE dbo.TenantCreditGrants
    SET RevokedAt = @RevokedAt
    WHERE Id = @Id
      AND RevokedAt IS NULL;

    DECLARE @Applied INT = @@ROWCOUNT;

    COMMIT TRANSACTION;

    SELECT @Applied;
END
GO

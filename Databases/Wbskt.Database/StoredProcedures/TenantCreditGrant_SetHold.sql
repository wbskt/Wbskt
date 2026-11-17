-- Holds the grant made for (@Source, @SourceRef) while a chargeback on its payment is open (@Held = 1), or
-- releases it (@Held = 0), when @Revision is newer than the one it holds: a hold or release delivered
-- late changes nothing. Returns 1 when it applied, 0 when there was no such grant or the revision was
-- not newer.
CREATE PROCEDURE dbo.TenantCreditGrant_SetHold
    @Source    NVARCHAR(40),
    @SourceRef NVARCHAR(200),
    @Held      BIT,
    @Revision  INT,
    @At        DATETIME2(3)
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
    SET HeldAt = CASE WHEN @Held = 1 THEN @At END,
        HoldRevision = @Revision
    WHERE Id = @Id
      AND HoldRevision < @Revision;

    DECLARE @Applied INT = @@ROWCOUNT;

    COMMIT TRANSACTION;

    SELECT @Applied;
END
GO

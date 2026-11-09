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

    UPDATE dbo.TenantCreditGrants
    SET HeldAt = CASE WHEN @Held = 1 THEN @At END,
        HoldRevision = @Revision
    WHERE Source = @Source
      AND SourceRef = @SourceRef
      AND HoldRevision < @Revision;

    SELECT @@ROWCOUNT;
END
GO

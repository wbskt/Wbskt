-- Grants @Credits to @TenantId until @ExpiresAt, once per (@Source, @SourceRef): a replayed payment or
-- a redelivered event adds nothing. Returns 1 when it granted, 0 when the grant already existed.
CREATE PROCEDURE dbo.TenantCreditGrant_Insert
    @TenantId  INT,
    @Credits   DECIMAL(18,4),
    @GrantedAt DATETIME2(3),
    @ExpiresAt DATETIME2(3),
    @Source    NVARCHAR(40),
    @SourceRef NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.TenantCreditGrants (TenantId, Credits, GrantedAt, ExpiresAt, Source, SourceRef)
    SELECT @TenantId, @Credits, @GrantedAt, @ExpiresAt, @Source, @SourceRef
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.TenantCreditGrants WITH (UPDLOCK, HOLDLOCK)
        WHERE Source = @Source AND SourceRef = @SourceRef);

    SELECT @@ROWCOUNT;
END
GO

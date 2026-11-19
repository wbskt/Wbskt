-- The second half of the last-administrator guard: refuses (THROW 50008) a change that left a tenant
-- that had an administrator with none. The caller has SET XACT_ABORT ON, so the throw rolls its change back.
CREATE PROCEDURE dbo.Tenant_AssertAdministered
    @TenantId INT,
    @HadAdministrator BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF @HadAdministrator = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50008, 'This change would leave the tenant without an administrator.', 1;
    END
END
GO

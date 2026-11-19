-- The first half of the last-administrator guard (see dbo.Tenant_Administrators for the rule): takes an
-- update lock on the tenant's row, held to the end of the caller's transaction, and says whether the
-- tenant has an administrator before the change. Call it inside the transaction, before the change, and
-- dbo.Tenant_AssertAdministered after it with the same @HadAdministrator.
CREATE PROCEDURE dbo.Tenant_LockAdministered
    @TenantId INT,
    @HadAdministrator BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;

    SET @HadAdministrator = IIF(EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId)), 1, 0);
END
GO

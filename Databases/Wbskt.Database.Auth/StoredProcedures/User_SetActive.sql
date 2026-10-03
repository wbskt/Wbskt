-- Enables or disables an account. Login and token refresh both reject inactive users, but the
-- caller is still responsible for revoking live refresh tokens when deactivating.
--
-- @TenantId is the tenant the request was made in. Deactivating is refused (THROW 50008) when it
-- would leave that tenant without an active administrator; see dbo.Tenant_Administrators. That
-- includes an only administrator deactivating themselves.
--
-- Only that tenant, not every tenant the account belongs to. The account is global, so this also
-- locks the user out of any tenant they administer elsewhere; checking those too would refuse to
-- deactivate nearly anyone, since registration gives every user a tenant of their own. Tenant-scoped
-- suspension replaces this as the tenant administrator's tool.
CREATE PROCEDURE dbo.User_SetActive
    @Id INT,
    @IsActive BIT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Last-administrator guard; see dbo.Tenant_Administrators for the rule and the lock.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;
    DECLARE @HadAdministrator BIT = IIF(EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId)), 1, 0);

    UPDATE dbo.Users
    SET IsActive = @IsActive
    WHERE Id = @Id;

    IF @HadAdministrator = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50008, 'This change would leave the tenant without an administrator.', 1;
    END

    COMMIT TRANSACTION;
END
GO

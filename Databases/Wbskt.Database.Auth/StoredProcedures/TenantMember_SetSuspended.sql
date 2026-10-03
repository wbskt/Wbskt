-- Suspends a member in one tenant, or lifts the suspension. Their account, their other tenants and
-- everything assigned to them here are untouched; while suspended, access resolution treats them as
-- holding nothing in this tenant (see the IsSuspended checks in Permission_Verify,
-- Permission_EffectiveSet and WorkspaceMember_Verify).
--
-- Refused (THROW 50008) when it would leave the tenant without an active administrator; see
-- dbo.Tenant_Administrators.
CREATE PROCEDURE dbo.TenantMember_SetSuspended
    @TenantId INT,
    @UserId INT,
    @IsSuspended BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Last-administrator guard; see dbo.Tenant_Administrators for the rule and the lock.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;
    DECLARE @HadAdministrator BIT = IIF(EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId)), 1, 0);

    UPDATE dbo.TenantMembers
    SET IsSuspended = @IsSuspended
    WHERE TenantId = @TenantId
      AND UserId = @UserId;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 50016, 'The user is not a member of this tenant.', 1;
    END

    IF @HadAdministrator = 1 AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId))
    BEGIN
        THROW 50008, 'This change would leave the tenant without an administrator.', 1;
    END

    COMMIT TRANSACTION;
END
GO

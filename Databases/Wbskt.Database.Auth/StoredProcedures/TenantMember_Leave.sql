-- A member leaves a tenant of their own accord (POST /api/v1/tenants/{tenantRef}/leave). The same
-- removal dbo.User_Delete makes from each shared tenant, for one tenant: dbo.TenantMember_Remove,
-- with the member's workspaces passing to another administrator. A suspended member may leave too.
--
-- Refused, so nobody is stranded:
-- * 50023: not a member of the tenant (or no such tenant; the two read the same).
-- * 50024: it is the only tenant the user belongs to. An account in no tenant can do nothing, and no
--   endpoint could repair it.
-- * 50025: nobody else belongs to it. Leaving would orphan the tenant and its workspaces.
-- * 50026: nobody else administers it. The message names the tenant.
--
-- Locks every tenant the user belongs to, as dbo.User_Delete does, so two leaves at once cannot each
-- count the other tenant as the one that remains, and an invitation accepted mid-way cannot slip in.
CREATE PROCEDURE dbo.TenantMember_Leave
    @TenantRef UNIQUEIDENTIFIER,
    @UserId    INT,
    @TenantId  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @Locked INT;
    SELECT @Locked = COUNT(*)
    FROM dbo.Tenants T WITH (UPDLOCK, HOLDLOCK)
    WHERE T.Id IN (SELECT TenantId FROM dbo.TenantMembers WHERE UserId = @UserId);

    SELECT @TenantId = T.Id
    FROM dbo.Tenants T
    INNER JOIN dbo.TenantMembers TM WITH (HOLDLOCK) ON TM.TenantId = T.Id
    WHERE T.RefId = @TenantRef
      AND TM.UserId = @UserId;

    IF @TenantId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50023, 'Tenant not found.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WITH (HOLDLOCK) WHERE UserId = @UserId AND TenantId <> @TenantId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50024, 'This is the only tenant you belong to, so you cannot leave it.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WITH (HOLDLOCK) WHERE TenantId = @TenantId AND UserId <> @UserId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50025, 'You are the only member of this tenant, so leaving would leave it with nobody.', 1;
    END

    DECLARE @NewOwnerUserId INT = (SELECT MIN(UserId) FROM dbo.Tenant_Administrators(@TenantId) WHERE UserId <> @UserId);

    IF @NewOwnerUserId IS NULL
    BEGIN
        DECLARE @Message NVARCHAR(2048) = LEFT(
            N'Nobody else administers ' + (SELECT Name FROM dbo.Tenants WHERE Id = @TenantId) + N'. Make another member an administrator before leaving.', 2048);
        ROLLBACK TRANSACTION;
        THROW 50026, @Message, 1;
    END

    EXEC dbo.TenantMember_Remove @TenantId = @TenantId, @UserId = @UserId, @NewOwnerUserId = @NewOwnerUserId;

    COMMIT TRANSACTION;
END
GO

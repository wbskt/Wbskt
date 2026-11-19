-- Removes a user from a tenant along with everything scoped to it: role and permission assignments,
-- group memberships, and membership of the tenant's workspaces. Removed explicitly rather than by
-- cascade, so that re-inviting someone later does not silently restore the access they had before.
--
-- The user's account, and their membership of any other tenant, are untouched. This is the
-- offboarding operation; disabling the account itself is staff's User_StaffSetActive and locks them
-- out of every tenant, including their own.
--
-- Workspaces they owned in this tenant transfer to @NewOwnerUserId, who is added as a member of each
-- if they were not already and given its Admin role, as an explicit transfer would. Leaving OwnerUserId pointing at a non-member would strand those
-- workspaces: access resolution gates on a WorkspaceMembers row before it evaluates any permission,
-- so a workspace with no member holding users.manage cannot be administered or deleted by anyone,
-- tenant-wide administrator included.
CREATE PROCEDURE dbo.TenantMember_Remove
    @TenantId INT,
    @UserId INT,
    @NewOwnerUserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserId = @NewOwnerUserId
    BEGIN
        THROW 50013, 'A member cannot be removed in favour of themselves.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @NewOwnerUserId)
    BEGIN
        THROW 50009, 'The receiving owner is not a member of this tenant.', 1;
    END

    BEGIN TRANSACTION;

    -- A tenant with no remaining administrator cannot be repaired through the API; see
    -- dbo.Tenant_Administrators, which also explains the lock. Checked inside the transaction and
    -- under the lock, so two concurrent removals cannot each count the other as the one remaining.
    --
    -- The caller holds tenant-wide users.manage and cannot target themselves, but that does not make
    -- them an administrator: one whose custom role carries users.manage without roles.manage would
    -- otherwise remove the only holder of the Admin role, and nobody could manage roles again (#948).
    -- It also covers an administrator whose permissions arrive through a group, which the function
    -- does not count, and any future caller that is not this endpoint.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;

    IF NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId) WHERE UserId <> @UserId)
    BEGIN
        THROW 50008, 'Cannot remove the last administrator of a tenant.', 1;
    END

    -- Ownership transfers before the membership rows are cleared, the same way an explicit transfer
    -- does (dbo.WorkspaceOwner_Grant): the receiving administrator becomes a member of each workspace
    -- they inherit and holds its Admin role, and each hand-over is queued for the audit log.
    DECLARE @HandedOver INT;
    DECLARE owned CURSOR LOCAL FAST_FORWARD FOR
        SELECT Id FROM dbo.Workspaces WHERE TenantId = @TenantId AND OwnerUserId = @UserId;
    OPEN owned;
    FETCH NEXT FROM owned INTO @HandedOver;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.WorkspaceOwner_Grant @WorkspaceId = @HandedOver, @UserId = @NewOwnerUserId, @TenantId = @TenantId, @Announce = 1;
        FETCH NEXT FROM owned INTO @HandedOver;
    END
    CLOSE owned;
    DEALLOCATE owned;

    DELETE FROM dbo.UserRoles WHERE UserId = @UserId AND TenantId = @TenantId;
    DELETE FROM dbo.UserPermissions WHERE UserId = @UserId AND TenantId = @TenantId;

    DELETE UG
    FROM dbo.UserGroups UG
    INNER JOIN dbo.Groups G ON G.Id = UG.GroupId
    WHERE UG.UserId = @UserId AND G.TenantId = @TenantId;

    DELETE WM
    FROM dbo.WorkspaceMembers WM
    INNER JOIN dbo.Workspaces W ON W.Id = WM.WorkspaceId
    WHERE WM.UserId = @UserId AND W.TenantId = @TenantId;

    -- Any invitation they have not yet redeemed for this tenant is moot now.
    UPDATE dbo.TenantInvitations
    SET RevokedAt = SYSUTCDATETIME()
    WHERE TenantId = @TenantId
      AND AcceptedAt IS NULL
      AND RevokedAt IS NULL
      AND Email = (SELECT Email FROM dbo.Users WHERE Id = @UserId);

    DELETE FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId;

    COMMIT TRANSACTION;
END
GO

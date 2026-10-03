-- Removes a user from a tenant along with everything scoped to it: role and permission assignments,
-- group memberships, and membership of the tenant's workspaces. Removed explicitly rather than by
-- cascade, so that re-inviting someone later does not silently restore the access they had before.
--
-- The user's account, and their membership of any other tenant, are untouched. This is the
-- offboarding operation; disabling the account itself is User_SetActive and locks them out of every
-- tenant, including their own.
--
-- Workspaces they owned in this tenant transfer to @NewOwnerUserId, who is added as a member of each
-- if they were not already. Leaving OwnerUserId pointing at a non-member would strand those
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
    -- Along the ordinary path this guard never fires: the caller must hold tenant-wide users.manage
    -- and cannot target themselves, so a remaining administrator is implied. It exists for the cases
    -- that do not hold -- an administrator whose users.manage arrives through a group, which the
    -- function does not count, and any future caller that is not this endpoint.
    DECLARE @TenantLock INT;
    SELECT @TenantLock = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TenantId;

    IF NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(@TenantId) WHERE UserId <> @UserId)
    BEGIN
        THROW 50008, 'Cannot remove the last administrator of a tenant.', 1;
    END

    -- Ownership transfers before the membership rows are cleared, so the receiving administrator is
    -- a member of every workspace they inherit.
    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
    SELECT W.Id, @NewOwnerUserId
    FROM dbo.Workspaces W
    WHERE W.TenantId = @TenantId
      AND W.OwnerUserId = @UserId
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WM WHERE WM.WorkspaceId = W.Id AND WM.UserId = @NewOwnerUserId);

    UPDATE dbo.Workspaces
    SET OwnerUserId = @NewOwnerUserId
    WHERE TenantId = @TenantId AND OwnerUserId = @UserId;

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

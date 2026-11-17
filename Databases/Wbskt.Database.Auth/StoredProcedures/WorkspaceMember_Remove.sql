-- Removes a member and any workspace-scoped assignments they held there, so that re-adding them
-- later does not silently restore permissions granted under the previous membership.
-- Tenant-wide assignments (WorkspaceId IS NULL) are deliberately untouched.
-- The owner cannot be removed; a workspace with no owner has no route back to being administered.
-- Returns Removed: 0 when the user was not a member, so nothing is announced for a removal that did
-- not happen.
CREATE PROCEDURE dbo.WorkspaceMember_Remove
    @WorkspaceId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = @WorkspaceId AND OwnerUserId = @UserId)
    BEGIN
        THROW 50006, 'The workspace owner cannot be removed.', 1;
    END

    BEGIN TRANSACTION;

    DELETE FROM dbo.UserRoles WHERE UserId = @UserId AND WorkspaceId = @WorkspaceId;
    DELETE FROM dbo.UserPermissions WHERE UserId = @UserId AND WorkspaceId = @WorkspaceId;
    DELETE FROM dbo.WorkspaceMembers WHERE WorkspaceId = @WorkspaceId AND UserId = @UserId;
    DECLARE @Removed BIT = IIF(@@ROWCOUNT > 0, 1, 0);

    COMMIT TRANSACTION;

    SELECT @Removed AS Removed;
END
GO

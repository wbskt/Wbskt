-- Hands a workspace to another member of its tenant. The new owner is added to the workspace if they
-- were not in it, since an owner who is not a member cannot reach what they own. The previous owner
-- stays a member; removing them is a separate, ordinary member removal now that they can be removed.
CREATE PROCEDURE dbo.Workspace_SetOwner
    @WorkspaceId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Workspaces WITH (UPDLOCK, HOLDLOCK) WHERE Id = @WorkspaceId);

    IF @TenantId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50004, 'Workspace does not exist.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50009, 'User is not a member of the tenant that owns this workspace.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = @WorkspaceId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
        VALUES (@WorkspaceId, @UserId);
    END

    UPDATE dbo.Workspaces
    SET OwnerUserId = @UserId
    WHERE Id = @WorkspaceId;

    COMMIT TRANSACTION;
END
GO

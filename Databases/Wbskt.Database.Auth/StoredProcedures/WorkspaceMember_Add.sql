-- Adds a user to a workspace, and to the owning tenant if they are not already a member.
-- Without the tenant row the user passes the workspace membership gate but resolves no roles
-- (every assignment is filtered by TenantId), and Tenant_GetForUser returns nothing for them.
CREATE PROCEDURE dbo.WorkspaceMember_Add
    @WorkspaceId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Workspaces WHERE Id = @WorkspaceId);

    IF @TenantId IS NULL
    BEGIN
        THROW 50004, 'Workspace does not exist.', 1;
    END

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.TenantMembers (TenantId, UserId)
        VALUES (@TenantId, @UserId);
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = @WorkspaceId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
        VALUES (@WorkspaceId, @UserId);
    END

    COMMIT TRANSACTION;
END
GO

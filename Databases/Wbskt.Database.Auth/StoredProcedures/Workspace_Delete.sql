-- Deletes a workspace and every assignment scoped to it. WorkspaceMembers is removed by its own
-- ON DELETE CASCADE; the assignment tables have no cascade, so they are cleared explicitly.
-- Callers outside the Auth database (clients, policies, workflows) are not touched here - the
-- workspace reference simply stops resolving for them.
CREATE PROCEDURE dbo.Workspace_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DELETE FROM dbo.UserRoles WHERE WorkspaceId = @Id;
    DELETE FROM dbo.GroupRoles WHERE WorkspaceId = @Id;
    DELETE FROM dbo.UserPermissions WHERE WorkspaceId = @Id;
    DELETE FROM dbo.Workspaces WHERE Id = @Id;

    COMMIT TRANSACTION;
END
GO

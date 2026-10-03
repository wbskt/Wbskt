-- Deletes a workspace and every assignment scoped to it. WorkspaceMembers is removed by its own
-- ON DELETE CASCADE; the assignment tables have no cascade, so they are cleared explicitly.
-- What the main database holds for the workspace (clients, policies, workflows, schedules) cannot be
-- reached from here; the auth host publishes WorkspaceDeletedEvent and the management host retires
-- it with dbo.Workspace_Retire.
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

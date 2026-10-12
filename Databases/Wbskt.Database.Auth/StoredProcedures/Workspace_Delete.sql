-- Deletes a workspace and every assignment scoped to it. WorkspaceMembers is removed by its own
-- ON DELETE CASCADE; the assignment tables have no cascade, so they are cleared explicitly.
-- What the main database holds for the workspace (clients, policies, workflows, schedules) cannot be
-- reached from here: the management host retires it with dbo.Workspace_Retire when it hears
-- WorkspaceDeletedEvent. That event goes into dbo.OutboxMessages in this transaction, so it is sent
-- even if the host stops or the broker is down right after the delete commits.
CREATE PROCEDURE dbo.Workspace_Delete
    @Id INT,
    @OutboxMessageType NVARCHAR(256) = NULL,
    @OutboxPayload NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DELETE FROM dbo.UserRoles WHERE WorkspaceId = @Id;
    DELETE FROM dbo.GroupRoles WHERE WorkspaceId = @Id;
    DELETE FROM dbo.UserPermissions WHERE WorkspaceId = @Id;
    DELETE FROM dbo.Workspaces WHERE Id = @Id;

    -- Only for a workspace that existed: a repeated delete announces nothing.
    IF @@ROWCOUNT > 0 AND @OutboxMessageType IS NOT NULL
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        VALUES (@OutboxMessageType, @OutboxPayload);
    END

    COMMIT TRANSACTION;
END
GO

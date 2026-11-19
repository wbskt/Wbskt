-- Deletes a workspace and every assignment scoped to it. WorkspaceMembers is removed by its own
-- ON DELETE CASCADE; the assignment tables have no cascade, so they are cleared explicitly.
-- What the main database holds for the workspace (clients, policies, workflows, schedules) cannot be
-- reached from here: the management host retires it with dbo.Workspace_Retire when it hears
-- WorkspaceDeletedEvent. That event goes into dbo.OutboxMessages in this transaction, so it is sent
-- even if the host stops or the broker is down right after the delete commits. The delete is also
-- recorded in dbo.DeletedWorkspaces, which the management host's daily sweep reads to catch a
-- retirement the event did not achieve, with what the admin views show of a deleted workspace.
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
    DECLARE @Deleted TABLE (
        RefId UNIQUEIDENTIFIER NOT NULL,
        TenantId INT NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        OwnerUserId INT NOT NULL,
        CreatedAt DATETIME2(3) NOT NULL);

    DELETE FROM dbo.Workspaces
    OUTPUT DELETED.RefId, DELETED.TenantId, DELETED.Name, DELETED.OwnerUserId, DELETED.CreatedAt
    INTO @Deleted (RefId, TenantId, Name, OwnerUserId, CreatedAt)
    WHERE Id = @Id;

    -- Only for a workspace that existed: a repeated delete records and announces nothing.
    IF EXISTS (SELECT 1 FROM @Deleted)
    BEGIN
        INSERT INTO dbo.DeletedWorkspaces (WorkspaceId, RefId, TenantId, Name, OwnerUserId, CreatedAt)
        SELECT @Id, D.RefId, D.TenantId, D.Name, D.OwnerUserId, D.CreatedAt
        FROM @Deleted D
        WHERE NOT EXISTS (SELECT 1 FROM dbo.DeletedWorkspaces WHERE WorkspaceId = @Id);

        IF @OutboxMessageType IS NOT NULL
        BEGIN
            INSERT INTO dbo.OutboxMessages (MessageType, Payload)
            VALUES (@OutboxMessageType, @OutboxPayload);
        END
    END

    COMMIT TRANSACTION;
END
GO

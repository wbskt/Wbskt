-- Drops every command a device has waiting, when it is revoked: it will not connect again to take them.
CREATE PROCEDURE dbo.QueuedCommand_DeleteBy_Client
    @ClientId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.QueuedCommands
    OUTPUT deleted.Id, deleted.CommandId, deleted.ClientId, deleted.ClientRefId, deleted.WorkspaceId, deleted.Type, deleted.Payload, deleted.ExpiresAt, deleted.QueuedAt,
        deleted.ActorUserId, deleted.ActorUserRefId, deleted.ActorSource, deleted.ActorWorkflowRefId, deleted.ActorRunRefId
    WHERE ClientId = @ClientId;
END
GO

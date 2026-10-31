-- Takes one waiting command out of the queue, scoped to its device. No row when it is not waiting
-- (already delivered, expired, cancelled, or another device's).
CREATE PROCEDURE dbo.QueuedCommand_Cancel
    @ClientId  INT,
    @CommandId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.QueuedCommands
    OUTPUT deleted.Id, deleted.CommandId, deleted.ClientId, deleted.ClientRefId, deleted.WorkspaceId, deleted.Type, deleted.Payload, deleted.ExpiresAt, deleted.QueuedAt,
        deleted.ActorUserId, deleted.ActorUserRefId, deleted.ActorSource, deleted.ActorWorkflowRefId, deleted.ActorRunRefId
    WHERE ClientId = @ClientId
      AND CommandId = @CommandId;
END
GO

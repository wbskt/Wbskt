-- A device's waiting commands, oldest first, for the client page's "waiting" list and count.
CREATE PROCEDURE dbo.QueuedCommand_GetBy_Client
    @ClientId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id, CommandId, ClientId, ClientRefId, WorkspaceId, Type, Payload, ExpiresAt, QueuedAt,
        ActorUserId, ActorUserRefId, ActorSource, ActorWorkflowRefId, ActorRunRefId
    FROM dbo.QueuedCommands
    WHERE ClientId = @ClientId
    ORDER BY Id;
END
GO

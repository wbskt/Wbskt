-- Takes a device's waiting commands out of the queue for delivery, when it has connected. Each row is
-- taken once, so a delivery started by the connect and one started by a sender that saw the device
-- come online never send the same command twice. Expired rows stay for the expiry sweep, which
-- reports them. The caller orders by Id (oldest first): DELETE ... OUTPUT has no order of its own.
CREATE PROCEDURE dbo.QueuedCommand_TakeBy_Client
    @ClientId INT,
    @Now      DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.QueuedCommands
    OUTPUT deleted.Id, deleted.CommandId, deleted.ClientId, deleted.ClientRefId, deleted.WorkspaceId, deleted.Type, deleted.Payload, deleted.ExpiresAt, deleted.QueuedAt,
        deleted.ActorUserId, deleted.ActorUserRefId, deleted.ActorSource, deleted.ActorWorkflowRefId, deleted.ActorRunRefId
    WHERE ClientId = @ClientId
      AND ExpiresAt > @Now;
END
GO

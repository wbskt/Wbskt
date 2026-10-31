-- Takes up to @Take commands whose expiry has passed while they waited, for the sweep to report as
-- failed. READPAST lets two sweeps share the work without waiting on each other's rows.
CREATE PROCEDURE dbo.QueuedCommand_TakeExpired
    @Now  DATETIME2(3),
    @Take INT = 200
AS
BEGIN
    SET NOCOUNT ON;

    DELETE TOP (@Take) FROM dbo.QueuedCommands WITH (READPAST)
    OUTPUT deleted.Id, deleted.CommandId, deleted.ClientId, deleted.ClientRefId, deleted.WorkspaceId, deleted.Type, deleted.Payload, deleted.ExpiresAt, deleted.QueuedAt,
        deleted.ActorUserId, deleted.ActorUserRefId, deleted.ActorSource, deleted.ActorWorkflowRefId, deleted.ActorRunRefId
    WHERE ExpiresAt <= @Now;
END
GO

-- Queues a command for an offline device, unless the device already has @MaxPerClient waiting.
-- The count and the insert are one locked step, so two senders cannot both take the last place.
-- Queueing a command id that is already waiting is a no-op, so a retried hand-back from a socket
-- host does not fail on the unique key.
-- Returns Outcome: 1 queued, 2 already waiting, 0 refused (the queue is full or the client is gone).
CREATE PROCEDURE dbo.QueuedCommand_Enqueue
    @CommandId          UNIQUEIDENTIFIER,
    @ClientId           INT,
    @ClientRefId        UNIQUEIDENTIFIER,
    @WorkspaceId        INT,
    @Type               NVARCHAR(100),
    @Payload            NVARCHAR(MAX),
    @ExpiresAt          DATETIME2(3),
    @ActorUserId        INT = NULL,
    @ActorUserRefId     UNIQUEIDENTIFIER = NULL,
    @ActorSource        TINYINT = NULL,
    @ActorWorkflowRefId UNIQUEIDENTIFIER = NULL,
    @ActorRunRefId      UNIQUEIDENTIFIER = NULL,
    @MaxPerClient       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.QueuedCommands WHERE CommandId = @CommandId)
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(2 AS TINYINT) AS Outcome;
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.Clients WHERE Id = @ClientId)
       OR (SELECT COUNT(*) FROM dbo.QueuedCommands WITH (UPDLOCK, HOLDLOCK) WHERE ClientId = @ClientId) >= @MaxPerClient
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(0 AS TINYINT) AS Outcome;
        RETURN;
    END

    INSERT INTO dbo.QueuedCommands
        (CommandId, ClientId, ClientRefId, WorkspaceId, Type, Payload, ExpiresAt,
         ActorUserId, ActorUserRefId, ActorSource, ActorWorkflowRefId, ActorRunRefId)
    VALUES
        (@CommandId, @ClientId, @ClientRefId, @WorkspaceId, @Type, @Payload, @ExpiresAt,
         @ActorUserId, @ActorUserRefId, @ActorSource, @ActorWorkflowRefId, @ActorRunRefId);

    COMMIT TRANSACTION;
    SELECT CAST(1 AS TINYINT) AS Outcome;
END
GO

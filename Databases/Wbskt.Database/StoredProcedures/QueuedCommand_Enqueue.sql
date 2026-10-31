-- Queues a command for an offline device, unless the device already has @MaxPerClient waiting.
-- The count and the insert are one locked step, so two senders cannot both take the last place.
-- Queueing a command id that is already waiting is a no-op that reports it queued, so a retried
-- hand-back from a socket host does not fail on the unique key.
-- Returns Queued = 1 when the command is waiting, 0 when the queue is full or the client is gone.
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
        SELECT CAST(1 AS BIT) AS Queued;
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.Clients WHERE Id = @ClientId)
       OR (SELECT COUNT(*) FROM dbo.QueuedCommands WITH (UPDLOCK, HOLDLOCK) WHERE ClientId = @ClientId) >= @MaxPerClient
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(0 AS BIT) AS Queued;
        RETURN;
    END

    INSERT INTO dbo.QueuedCommands
        (CommandId, ClientId, ClientRefId, WorkspaceId, Type, Payload, ExpiresAt,
         ActorUserId, ActorUserRefId, ActorSource, ActorWorkflowRefId, ActorRunRefId)
    VALUES
        (@CommandId, @ClientId, @ClientRefId, @WorkspaceId, @Type, @Payload, @ExpiresAt,
         @ActorUserId, @ActorUserRefId, @ActorSource, @ActorWorkflowRefId, @ActorRunRefId);

    COMMIT TRANSACTION;
    SELECT CAST(1 AS BIT) AS Queued;
END
GO

CREATE PROCEDURE dbo.ScheduledFire_Insert
    @TriggerNodeId        UNIQUEIDENTIFIER,
    @WorkflowDefinitionId INT,
    @WorkflowRefId        UNIQUEIDENTIFIER,
    @CronOrInterval       NVARCHAR(200),
    @NextFireAt           DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NewId INT;

    BEGIN TRANSACTION;

    -- Registering the same version twice returns the schedule it already has rather than adding a
    -- second one; the range lock keeps two concurrent registrations from both missing it.
    SELECT @NewId = Id
    FROM dbo.ScheduledFires WITH (UPDLOCK, HOLDLOCK)
    WHERE WorkflowDefinitionId = @WorkflowDefinitionId
      AND TriggerNodeId = @TriggerNodeId;

    IF @NewId IS NULL
    BEGIN
        INSERT INTO dbo.ScheduledFires
            (TriggerNodeId, WorkflowDefinitionId, WorkflowRefId, CronOrInterval, NextFireAt)
        VALUES
            (@TriggerNodeId, @WorkflowDefinitionId, @WorkflowRefId, @CronOrInterval, @NextFireAt);

        SET @NewId = SCOPE_IDENTITY();
    END;

    COMMIT TRANSACTION;

    SELECT
        Id,
        TriggerNodeId,
        WorkflowDefinitionId,
        WorkflowRefId,
        CronOrInterval,
        NextFireAt,
        LeasedUntil,
        CreatedAt
    FROM dbo.ScheduledFires
    WHERE Id = @NewId;
END;
GO

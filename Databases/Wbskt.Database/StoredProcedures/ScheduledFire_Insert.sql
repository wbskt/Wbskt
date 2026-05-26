CREATE PROCEDURE dbo.ScheduledFire_Insert
    @TriggerNodeId        UNIQUEIDENTIFIER,
    @WorkflowDefinitionId INT,
    @WorkflowRefId        UNIQUEIDENTIFIER,
    @CronOrInterval       NVARCHAR(200),
    @NextFireAt           DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ScheduledFires
        (TriggerNodeId, WorkflowDefinitionId, WorkflowRefId, CronOrInterval, NextFireAt)
    VALUES
        (@TriggerNodeId, @WorkflowDefinitionId, @WorkflowRefId, @CronOrInterval, @NextFireAt);

    DECLARE @NewId INT = SCOPE_IDENTITY();

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

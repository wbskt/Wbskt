CREATE PROCEDURE dbo.ScheduledFire_AdvanceNext
    @Id         INT,
    @NextFireAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ScheduledFires
    SET NextFireAt  = @NextFireAt,
        LeasedUntil = NULL
    WHERE Id = @Id;

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
    WHERE Id = @Id;
END;
GO

CREATE PROCEDURE dbo.ScheduledFire_LeaseDue
    @LeaseSec INT = 60,
    @Batch    INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TOP (@Batch) dbo.ScheduledFires WITH (UPDLOCK, READPAST)
    SET LeasedUntil = DATEADD(SECOND, @LeaseSec, SYSUTCDATETIME())
    OUTPUT
        inserted.Id,
        inserted.TriggerNodeId,
        inserted.WorkflowDefinitionId,
        inserted.WorkflowRefId,
        inserted.CronOrInterval,
        inserted.NextFireAt,
        inserted.LeasedUntil,
        inserted.CreatedAt
    WHERE NextFireAt <= SYSUTCDATETIME()
      AND (LeasedUntil IS NULL OR LeasedUntil < SYSUTCDATETIME());
END;
GO

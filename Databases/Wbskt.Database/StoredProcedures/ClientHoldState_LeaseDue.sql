CREATE PROCEDURE dbo.ClientHoldState_LeaseDue
    @LeaseSec INT = 15,
    @Batch    INT = 64
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TOP (@Batch) dbo.ClientHoldStates WITH (UPDLOCK, READPAST)
    SET LeasedUntil = DATEADD(SECOND, @LeaseSec, SYSUTCDATETIME())
    OUTPUT
        inserted.Id,
        inserted.TriggerKey,
        inserted.SinceAt,
        inserted.DueAt,
        inserted.Payload
    WHERE State = N'holding'
      AND DueAt <= SYSUTCDATETIME()
      AND (LeasedUntil IS NULL OR LeasedUntil < SYSUTCDATETIME());
END;
GO

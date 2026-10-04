-- Retention sweep for readings: deletes up to @BatchSize rows received before @CutoffUtc and returns
-- how many went. The caller repeats until a batch comes back short, as for dbo.EventLogs_DeleteBefore.
CREATE PROCEDURE dbo.ClientReadings_DeleteBefore
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE TOP (@BatchSize)
    FROM dbo.ClientReadings WITH (READPAST)
    WHERE ReceivedAt < @CutoffUtc;

    SELECT @@ROWCOUNT AS Deleted;
END
GO

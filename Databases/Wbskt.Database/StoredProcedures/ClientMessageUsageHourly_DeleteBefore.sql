-- Retention sweep for the per-client hourly message counts: deletes up to @BatchSize hours older than
-- @CutoffUtc and returns how many went. The caller repeats until a batch comes back short.
CREATE PROCEDURE dbo.ClientMessageUsageHourly_DeleteBefore
    @CutoffUtc DATETIME2(0),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE TOP (@BatchSize)
    FROM dbo.ClientMessageUsageHourly
    WHERE [Hour] < @CutoffUtc;

    SELECT @@ROWCOUNT AS Deleted;
END
GO

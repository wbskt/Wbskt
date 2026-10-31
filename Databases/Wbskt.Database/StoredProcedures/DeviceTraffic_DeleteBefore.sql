-- Retention sweep for device traffic: deletes rows older than @CutoffUtc from among the @BatchSize
-- oldest, and returns how many went. The caller repeats until a batch comes back short. Ids grow
-- with time, so the oldest rows are the front of the clustered key: each batch is a short seek
-- rather than a search, and a sweep with nothing to do reads one batch and stops. READPAST lets
-- several management hosts sweep at once.
CREATE PROCEDURE dbo.DeviceTraffic_DeleteBefore
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Oldest AS (
        SELECT TOP (@BatchSize) Id, CreatedAt
        FROM dbo.DeviceTraffic WITH (READPAST)
        ORDER BY Id
    )
    DELETE FROM Oldest
    WHERE CreatedAt < @CutoffUtc;

    SELECT @@ROWCOUNT AS Deleted;
END
GO

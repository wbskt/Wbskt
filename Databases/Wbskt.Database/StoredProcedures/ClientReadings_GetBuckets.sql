-- One variable's readings over [@FromUtc, @ToUtc), summarised per @BucketSeconds. Buckets are
-- counted from @FromUtc (the caller aligns it), and only buckets with readings come back.
CREATE PROCEDURE dbo.ClientReadings_GetBuckets
    @ClientId      INT,
    @Name          NVARCHAR(100),
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3),
    @BucketSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        DATEADD(SECOND, CAST(b.Bucket * @BucketSeconds AS INT), @FromUtc) AS BucketStart,
        COUNT_BIG(*)       AS ReadingCount,
        MIN(r.NumberValue) AS MinValue,
        AVG(r.NumberValue) AS AvgValue,
        MAX(r.NumberValue) AS MaxValue
    FROM dbo.ClientReadings r
    CROSS APPLY (SELECT DATEDIFF_BIG(SECOND, @FromUtc, r.DeviceTime) / @BucketSeconds AS Bucket) b
    WHERE r.ClientId = @ClientId
      AND r.Name = @Name
      AND r.DeviceTime >= @FromUtc
      AND r.DeviceTime < @ToUtc
    GROUP BY b.Bucket
    ORDER BY b.Bucket;
END
GO

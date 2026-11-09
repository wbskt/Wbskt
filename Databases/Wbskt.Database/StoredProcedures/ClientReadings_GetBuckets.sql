-- One variable's readings over [@FromUtc, @ToUtc), summarised per @BucketSeconds. Buckets are
-- counted from @FromUtc (the caller aligns it), and only buckets with readings come back.
--
-- Reads the raw readings and the hourly roll-up together: dbo.ClientReadings_Compact moves a reading
-- from one to the other in one statement, so none is counted twice. An hour rolled up lands whole in
-- the bucket its start falls in, so a bucket shorter than an hour shows rolled-up history at an
-- hour's resolution.
CREATE PROCEDURE dbo.ClientReadings_GetBuckets
    @ClientId      INT,
    @Name          NVARCHAR(100),
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3),
    @BucketSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Points AS (
        SELECT r.SentAt AS At, CAST(1 AS BIGINT) AS ReadingCount, r.NumberValue AS MinValue, r.NumberValue AS MaxValue, r.NumberValue AS SumValue
        FROM dbo.ClientReadings r
        WHERE r.ClientId = @ClientId
          AND r.Name = @Name
          AND r.SentAt >= @FromUtc
          AND r.SentAt < @ToUtc
        UNION ALL
        SELECT h.HourStart, h.ReadingCount, h.MinValue, h.MaxValue, h.SumValue
        FROM dbo.ClientReadingsHourly h
        WHERE h.ClientId = @ClientId
          AND h.Name = @Name
          AND h.HourStart >= @FromUtc
          AND h.HourStart < @ToUtc
    )
    SELECT
        DATEADD(SECOND, CAST(b.Bucket * @BucketSeconds AS INT), @FromUtc) AS BucketStart,
        SUM(p.ReadingCount)                  AS ReadingCount,
        MIN(p.MinValue)                      AS MinValue,
        SUM(p.SumValue) / SUM(p.ReadingCount) AS AvgValue,
        MAX(p.MaxValue)                      AS MaxValue
    FROM Points p
    CROSS APPLY (SELECT DATEDIFF_BIG(SECOND, @FromUtc, p.At) / @BucketSeconds AS Bucket) b
    GROUP BY b.Bucket
    ORDER BY b.Bucket;
END
GO

-- The readings over [@FromUtc, @ToUtc) summarised per variable and hour, oldest first, for export:
-- one variable, or every variable when @Name is NULL. Reads the raw readings and the hourly roll-up
-- together, as dbo.ClientReadings_GetBuckets does, so an export reaches as far back as the chart:
-- past the plan's raw days only the roll-up is left. At most @MaxRows; the caller asks for one more
-- than it will return to tell a complete range from a cut-off one.
CREATE PROCEDURE dbo.ClientReadings_GetHourlyRange
    @ClientId INT,
    @Name     NVARCHAR(100) = NULL,
    @FromUtc  DATETIME2(3),
    @ToUtc    DATETIME2(3),
    @MaxRows  INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH Points AS (
        SELECT
            r.Name,
            DATEADD(HOUR, DATEDIFF(HOUR, '2000-01-01', r.SentAt), CAST('2000-01-01' AS DATETIME2(0))) AS HourStart,
            CAST(1 AS BIGINT) AS ReadingCount,
            r.NumberValue AS MinValue,
            r.NumberValue AS MaxValue,
            r.NumberValue AS SumValue
        FROM dbo.ClientReadings r
        WHERE r.ClientId = @ClientId
          AND (@Name IS NULL OR r.Name = @Name)
          AND r.SentAt >= @FromUtc
          AND r.SentAt < @ToUtc
        UNION ALL
        SELECT h.Name, h.HourStart, h.ReadingCount, h.MinValue, h.MaxValue, h.SumValue
        FROM dbo.ClientReadingsHourly h
        WHERE h.ClientId = @ClientId
          AND (@Name IS NULL OR h.Name = @Name)
          AND h.HourStart >= @FromUtc
          AND h.HourStart < @ToUtc
    )
    SELECT TOP (@MaxRows)
        Name,
        HourStart,
        SUM(ReadingCount)                 AS ReadingCount,
        MIN(MinValue)                     AS MinValue,
        SUM(SumValue) / SUM(ReadingCount) AS AvgValue,
        MAX(MaxValue)                     AS MaxValue
    FROM Points
    GROUP BY Name, HourStart
    ORDER BY HourStart, Name
    OPTION (RECOMPILE);
END
GO

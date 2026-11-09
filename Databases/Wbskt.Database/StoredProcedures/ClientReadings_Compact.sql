-- One batch of the readings retention sweep, by each client's plan (@Retention, overridden by the
-- tenant's rawReadingsDays and hourlyReadingsDays where it has them; a workspace with no account is
-- Free):
--   1. Raw readings received before the plan's raw cutoff are deleted and rolled into
--      dbo.ClientReadingsHourly in the same transaction, so each reading is counted once, in one table.
--   2. Hourly rows older than the plan's hourly cutoff are deleted.
--   3. A deleted client's raw and hourly readings are cleared, and its dbo.DeletedClientReadings row
--      dropped once none are left.
-- Up to @BatchSize rows each; returns how many went in each step. The caller repeats while any step
-- came back full.
CREATE PROCEDURE dbo.ClientReadings_Compact
    @Now       DATETIME2(3),
    @Retention dbo.ReadingRetentionTableType READONLY,
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Epoch DATETIME2(0) = '2000-01-01';
    DECLARE @RolledUp INT, @HourlyDeleted INT, @Cleared INT;
    DECLARE @Gone TABLE (ClientId INT NOT NULL, Name NVARCHAR(100) NOT NULL, SentAt DATETIME2(3) NOT NULL, NumberValue FLOAT NOT NULL);

    CREATE TABLE #Cutoffs (
        ClientId     INT          NOT NULL PRIMARY KEY,
        RawCutoff    DATETIME2(3) NOT NULL,
        HourlyCutoff DATETIME2(3) NOT NULL
    );

    INSERT INTO #Cutoffs (ClientId, RawCutoff, HourlyCutoff)
    SELECT
        c.Id,
        DATEADD(DAY, -COALESCE(TRY_CAST(JSON_VALUE(a.LimitOverrides, '$.rawReadingsDays') AS INT), p.RawDays), @Now),
        DATEADD(DAY, -COALESCE(TRY_CAST(JSON_VALUE(a.LimitOverrides, '$.hourlyReadingsDays') AS INT), p.HourlyDays), @Now)
    FROM dbo.Clients c
    LEFT JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = c.WorkspaceId
    JOIN @Retention p ON p.PlanCode = COALESCE(a.PlanCode, N'Free');

    BEGIN TRANSACTION;

    DELETE TOP (@BatchSize) r
    OUTPUT deleted.ClientId, deleted.Name, deleted.SentAt, deleted.NumberValue INTO @Gone
    FROM dbo.ClientReadings r
    JOIN #Cutoffs c ON c.ClientId = r.ClientId
    WHERE r.ReceivedAt < c.RawCutoff;

    SET @RolledUp = @@ROWCOUNT;

    -- Added to an hour already rolled up rather than replacing it: a late reading, or a batch that
    -- split the hour, adds its count, sum and extremes.
    MERGE dbo.ClientReadingsHourly WITH (HOLDLOCK) AS h
    USING (
        SELECT
            ClientId,
            Name,
            DATEADD(HOUR, DATEDIFF(HOUR, @Epoch, SentAt), @Epoch) AS HourStart,
            COUNT(*)         AS ReadingCount,
            MIN(NumberValue) AS MinValue,
            MAX(NumberValue) AS MaxValue,
            SUM(NumberValue) AS SumValue
        FROM @Gone
        GROUP BY ClientId, Name, DATEADD(HOUR, DATEDIFF(HOUR, @Epoch, SentAt), @Epoch)
    ) AS s
       ON h.ClientId = s.ClientId AND h.Name = s.Name AND h.HourStart = s.HourStart
    WHEN MATCHED THEN
        UPDATE SET
            ReadingCount = h.ReadingCount + s.ReadingCount,
            MinValue = CASE WHEN s.MinValue < h.MinValue THEN s.MinValue ELSE h.MinValue END,
            MaxValue = CASE WHEN s.MaxValue > h.MaxValue THEN s.MaxValue ELSE h.MaxValue END,
            SumValue = h.SumValue + s.SumValue
    WHEN NOT MATCHED THEN
        INSERT (ClientId, Name, HourStart, ReadingCount, MinValue, MaxValue, SumValue)
        VALUES (s.ClientId, s.Name, s.HourStart, s.ReadingCount, s.MinValue, s.MaxValue, s.SumValue);

    COMMIT TRANSACTION;

    DELETE TOP (@BatchSize) h
    FROM dbo.ClientReadingsHourly h
    JOIN #Cutoffs c ON c.ClientId = h.ClientId
    WHERE h.HourStart < c.HourlyCutoff;

    SET @HourlyDeleted = @@ROWCOUNT;

    DELETE TOP (@BatchSize) r
    FROM dbo.ClientReadings r
    JOIN dbo.DeletedClientReadings d ON d.ClientId = r.ClientId;

    SET @Cleared = @@ROWCOUNT;

    IF @Cleared < @BatchSize
    BEGIN
        DELETE TOP (@BatchSize - @Cleared) h
        FROM dbo.ClientReadingsHourly h
        JOIN dbo.DeletedClientReadings d ON d.ClientId = h.ClientId;

        SET @Cleared += @@ROWCOUNT;
    END

    DELETE d
    FROM dbo.DeletedClientReadings d
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ClientReadings r WHERE r.ClientId = d.ClientId)
      AND NOT EXISTS (SELECT 1 FROM dbo.ClientReadingsHourly h WHERE h.ClientId = d.ClientId);

    SELECT @RolledUp AS RolledUp, @HourlyDeleted AS HourlyDeleted, @Cleared AS Cleared;
END
GO

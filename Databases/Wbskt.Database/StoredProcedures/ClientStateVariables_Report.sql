-- Stores one state report in a single round trip, and returns one row per reported variable in the
-- order reported: its previous value (NULL when new) so the caller can emit ClientPropertyUpdatedEvent
-- for changes only, Stored = 0 for a new name refused at the cap, and Stale = 1 for a variable that
-- already holds a value from a later report.
-- Reports are consumed in no fixed order (retries, and a client's buffer sent after it reconnects), so
-- @ReportedAt, when the client sent the report, decides: a variable is not set back by an older one.
-- UpdatedAt is bumped on every applied value: it drives the console's freshness display ("4s ago").
-- A new name is stored only while the client has fewer than @MaxPerClient variables, so a client
-- cannot grow the table without bound by varying names; earlier names in the report win the last places.
CREATE PROCEDURE dbo.ClientStateVariables_Report
    @ClientId INT,
    @Variables dbo.ClientStateVariableTableType READONLY,
    @ReportedAt DATETIME2(3),
    @MaxPerClient INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Existing TABLE (Name NVARCHAR(100) NOT NULL PRIMARY KEY, Stale BIT NOT NULL);
    DECLARE @Result TABLE (Name NVARCHAR(100) NOT NULL PRIMARY KEY, OldValueJson NVARCHAR(MAX) NULL);

    BEGIN TRANSACTION;

    -- A row stored before ReportedAt was kept has no time, and any report may replace it.
    INSERT INTO @Existing (Name, Stale)
    SELECT v.Name, IIF(t.ReportedAt > @ReportedAt, 1, 0)
    FROM dbo.ClientStateVariables t WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN @Variables v ON v.Name = t.Name
    WHERE t.ClientId = @ClientId;

    UPDATE t
    SET
        ValueJson = v.ValueJson,
        DataType = v.DataType,
        ReportedAt = @ReportedAt,
        UpdatedAt = SYSUTCDATETIME()
    OUTPUT v.Name, deleted.ValueJson INTO @Result (Name, OldValueJson)
    FROM dbo.ClientStateVariables t
    INNER JOIN @Variables v ON v.Name = t.Name
    INNER JOIN @Existing e ON e.Name = v.Name AND e.Stale = 0
    WHERE t.ClientId = @ClientId;

    DECLARE @Count INT = (SELECT COUNT(*) FROM dbo.ClientStateVariables WITH (UPDLOCK, HOLDLOCK) WHERE ClientId = @ClientId);

    INSERT INTO dbo.ClientStateVariables (ClientId, Name, DataType, ValueJson, ReportedAt)
    OUTPUT inserted.Name, NULL INTO @Result (Name, OldValueJson)
    SELECT @ClientId, n.Name, n.DataType, n.ValueJson, @ReportedAt
    FROM (
        SELECT v.Name, v.DataType, v.ValueJson, ROW_NUMBER() OVER (ORDER BY v.Ordinal) AS Position
        FROM @Variables v
        WHERE NOT EXISTS (SELECT 1 FROM @Existing e WHERE e.Name = v.Name)
    ) n
    WHERE @Count + n.Position <= @MaxPerClient;

    COMMIT TRANSACTION;

    SELECT
        v.Name,
        r.OldValueJson,
        CAST(IIF(r.Name IS NULL, 0, 1) AS BIT) AS Stored,
        CAST(ISNULL(e.Stale, 0) AS BIT) AS Stale
    FROM @Variables v
    LEFT JOIN @Result r ON r.Name = v.Name
    LEFT JOIN @Existing e ON e.Name = v.Name
    ORDER BY v.Ordinal;
END
GO

-- Stores one state report in a single round trip, and returns one row per reported variable in the
-- order reported: its previous value (NULL when new) so the caller can emit ClientPropertyUpdatedEvent
-- for changes only, and Stored = 0 for a new name refused at the cap.
-- UpdatedAt is always bumped: it drives the console's freshness display ("4s ago").
-- A new name is stored only while the client has fewer than @MaxPerClient variables, so a client
-- cannot grow the table without bound by varying names; earlier names in the report win the last places.
CREATE PROCEDURE dbo.ClientStateVariables_Report
    @ClientId INT,
    @Variables dbo.ClientStateVariableTableType READONLY,
    @MaxPerClient INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Result TABLE (Name NVARCHAR(100) NOT NULL PRIMARY KEY, OldValueJson NVARCHAR(MAX) NULL);

    BEGIN TRANSACTION;

    UPDATE t
    SET
        ValueJson = v.ValueJson,
        DataType = v.DataType,
        UpdatedAt = SYSUTCDATETIME()
    OUTPUT v.Name, deleted.ValueJson INTO @Result (Name, OldValueJson)
    FROM dbo.ClientStateVariables t WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN @Variables v ON v.Name = t.Name
    WHERE t.ClientId = @ClientId;

    DECLARE @Count INT = (SELECT COUNT(*) FROM dbo.ClientStateVariables WITH (UPDLOCK, HOLDLOCK) WHERE ClientId = @ClientId);

    INSERT INTO dbo.ClientStateVariables (ClientId, Name, DataType, ValueJson)
    OUTPUT inserted.Name, NULL INTO @Result (Name, OldValueJson)
    SELECT @ClientId, n.Name, n.DataType, n.ValueJson
    FROM (
        SELECT v.*, ROW_NUMBER() OVER (ORDER BY v.Ordinal) AS Position
        FROM @Variables v
        WHERE NOT EXISTS (SELECT 1 FROM @Result r WHERE r.Name = v.Name)
    ) n
    WHERE @Count + n.Position <= @MaxPerClient;

    COMMIT TRANSACTION;

    SELECT v.Name, r.OldValueJson, CAST(IIF(r.Name IS NULL, 0, 1) AS BIT) AS Stored
    FROM @Variables v
    LEFT JOIN @Result r ON r.Name = v.Name
    ORDER BY v.Ordinal;
END
GO

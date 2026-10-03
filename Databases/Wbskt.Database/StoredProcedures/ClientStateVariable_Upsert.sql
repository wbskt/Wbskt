-- Upserts one state variable and returns the previous value (NULL on first report) so the
-- caller can detect changes and emit ClientPropertyUpdatedEvent with OldValue.
-- UpdatedAt is always bumped: it drives the console's freshness display ("4s ago").
-- A new name is stored only while the client has fewer than @MaxPerClient variables, so a device
-- cannot grow the table without bound by varying names; Stored = 0 reports the refusal.
CREATE PROCEDURE dbo.ClientStateVariable_Upsert
    @ClientId INT,
    @Name NVARCHAR(100),
    @DataType NVARCHAR(20),
    @ValueJson NVARCHAR(MAX),
    @MaxPerClient INT = 200
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldValueJson NVARCHAR(MAX);
    DECLARE @Stored BIT = 1;

    -- The variable assignment reads the pre-update value of the same row.
    UPDATE dbo.ClientStateVariables
    SET
        @OldValueJson = ValueJson,
        ValueJson = @ValueJson,
        DataType = @DataType,
        UpdatedAt = SYSUTCDATETIME()
    WHERE ClientId = @ClientId AND Name = @Name;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.ClientStateVariables (ClientId, Name, DataType, ValueJson)
        SELECT @ClientId, @Name, @DataType, @ValueJson
        WHERE (SELECT COUNT(*) FROM dbo.ClientStateVariables WHERE ClientId = @ClientId) < @MaxPerClient;

        SET @Stored = IIF(@@ROWCOUNT = 1, 1, 0);
    END

    SELECT @OldValueJson AS OldValueJson, @Stored AS Stored;
END
GO

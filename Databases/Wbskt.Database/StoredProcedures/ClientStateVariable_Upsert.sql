-- Upserts one state variable and returns the previous value (NULL on first report) so the
-- caller can detect changes and emit ClientPropertyUpdatedEvent with OldValue.
-- UpdatedAt is always bumped: it drives the console's freshness display ("4s ago").
CREATE PROCEDURE dbo.ClientStateVariable_Upsert
    @ClientId INT,
    @Name NVARCHAR(100),
    @DataType NVARCHAR(20),
    @ValueJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldValueJson NVARCHAR(MAX);

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
        VALUES (@ClientId, @Name, @DataType, @ValueJson);
    END

    SELECT @OldValueJson AS OldValueJson;
END
GO

-- The raw readings over [@FromUtc, @ToUtc), oldest first, for export: one variable, or every
-- variable when @Name is NULL. At most @MaxRows; the caller asks for one more than it will return
-- to tell a complete range from a cut-off one.
CREATE PROCEDURE dbo.ClientReadings_GetRange
    @ClientId INT,
    @Name     NVARCHAR(100) = NULL,
    @FromUtc  DATETIME2(3),
    @ToUtc    DATETIME2(3),
    @MaxRows  INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@MaxRows) Name, DeviceTime, ReceivedAt, NumberValue, IsLate
    FROM dbo.ClientReadings
    WHERE ClientId = @ClientId
      AND (@Name IS NULL OR Name = @Name)
      AND DeviceTime >= @FromUtc
      AND DeviceTime < @ToUtc
    ORDER BY DeviceTime, Name
    OPTION (RECOMPILE);
END
GO

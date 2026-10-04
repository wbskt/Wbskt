-- Stores the numbers from one state report. They share the report's device time, receive time and
-- lateness, so those come once rather than per row.
CREATE PROCEDURE dbo.ClientReadings_InsertBatch
    @ClientId   INT,
    @DeviceTime DATETIME2(3),
    @ReceivedAt DATETIME2(3),
    @IsLate     BIT,
    @Readings   dbo.ClientReadingTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ClientReadings (ClientId, Name, DeviceTime, ReceivedAt, NumberValue, IsLate)
    SELECT @ClientId, Name, @DeviceTime, @ReceivedAt, NumberValue, @IsLate
    FROM @Readings;
END
GO

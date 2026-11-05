-- Stores the numbers from one state report. They share the report's client time, receive time and
-- lateness, so those come once rather than per row. @DeviceTime is the old name of @SentAt, accepted
-- for one release so a host on the previous image keeps writing during a deploy (wbskt/Wbskt#583).
CREATE PROCEDURE dbo.ClientReadings_InsertBatch
    @ClientId   INT,
    @SentAt     DATETIME2(3) = NULL,
    @ReceivedAt DATETIME2(3),
    @IsLate     BIT,
    @Readings   dbo.ClientReadingTableType READONLY,
    @DeviceTime DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @SentAt = COALESCE(@SentAt, @DeviceTime);

    INSERT INTO dbo.ClientReadings (ClientId, Name, SentAt, ReceivedAt, NumberValue, IsLate)
    SELECT @ClientId, Name, @SentAt, @ReceivedAt, NumberValue, @IsLate
    FROM @Readings;
END
GO

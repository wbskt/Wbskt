-- Stores the numbers from one state report. They share the report's client time, receive time and
-- lateness, so those come once rather than per row.
CREATE PROCEDURE dbo.ClientReadings_InsertBatch
    @ClientId   INT,
    @SentAt     DATETIME2(3),
    @ReceivedAt DATETIME2(3),
    @IsLate     BIT,
    @Readings   dbo.ClientReadingTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ClientReadings (ClientId, Name, SentAt, ReceivedAt, NumberValue, IsLate)
    SELECT @ClientId, Name, @SentAt, @ReceivedAt, NumberValue, @IsLate
    FROM @Readings;
END
GO

-- Stores the numbers from one state report. They share the report's client time, receive time and
-- lateness, so those come once rather than per row. A report from a client deleted meanwhile is dropped,
-- so nothing lands after its history was queued for clearing (dbo.DeletedClientReadings).
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
    FROM @Readings
    WHERE EXISTS (SELECT 1 FROM dbo.Clients WHERE Id = @ClientId);
END
GO

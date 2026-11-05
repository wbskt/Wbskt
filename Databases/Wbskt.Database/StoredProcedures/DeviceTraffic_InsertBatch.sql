-- Old name of dbo.ClientTraffic_InsertBatch, kept for one release so hosts still on the previous
-- image keep logging while a deploy rolls through them (wbskt/Wbskt#583). Drop it in the release after.
CREATE PROCEDURE dbo.DeviceTraffic_InsertBatch
    @Logs dbo.EventLogTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.ClientTraffic_InsertBatch @Logs = @Logs;
END
GO

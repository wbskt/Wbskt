-- Old name of dbo.ClientTraffic_DeleteBefore, kept for one release so a host still on the previous
-- image can finish a retention sweep during a deploy (wbskt/Wbskt#583). Drop it in the release after.
CREATE PROCEDURE dbo.DeviceTraffic_DeleteBefore
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.ClientTraffic_DeleteBefore @CutoffUtc = @CutoffUtc, @BatchSize = @BatchSize;
END
GO

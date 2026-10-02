-- Timer and timeout bookmarks that fell due before @Cutoff and are still unclaimed. The leader's
-- poller claims due bookmarks within seconds, so anything here means it is stuck, behind, or
-- missing - runs that should have woken are silently waiting.
CREATE PROCEDURE dbo.Bookmark_CountOverdue
    @Cutoff DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT_BIG(*)
    FROM dbo.Bookmarks
    WHERE ExpiresAt <= @Cutoff;
END;
GO

CREATE PROCEDURE dbo.Bookmark_CountGroupedByWakeKind
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WakeConditionKind,
        COUNT(*) AS [Count]
    FROM dbo.Bookmarks
    GROUP BY WakeConditionKind;
END;
GO

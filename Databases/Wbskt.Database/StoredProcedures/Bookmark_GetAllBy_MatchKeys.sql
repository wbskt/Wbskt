CREATE PROCEDURE dbo.Bookmark_GetAllBy_MatchKeys
    @MatchKeysJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        RefId,
        RunId,
        BranchRefId,
        NodeId,
        WakeConditionKind,
        MatchKey,
        WakeConditionJson,
        ExpiresAt,
        TtlPort,
        CreatedAt
    FROM dbo.Bookmarks
    WHERE MatchKey IN (
        SELECT [value]
        FROM OPENJSON(@MatchKeysJson)
    );
END;
GO

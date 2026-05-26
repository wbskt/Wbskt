CREATE PROCEDURE dbo.Bookmark_GetAllBy_MatchKey
    @MatchKey NVARCHAR(400)
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
    WHERE MatchKey = @MatchKey;
END;
GO

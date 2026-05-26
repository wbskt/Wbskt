CREATE PROCEDURE dbo.Bookmark_GetAllBy_RunId
    @RunId INT
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
    WHERE RunId = @RunId;
END;
GO

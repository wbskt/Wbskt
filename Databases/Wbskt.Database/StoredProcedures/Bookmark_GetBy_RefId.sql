CREATE PROCEDURE dbo.Bookmark_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
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
    WHERE RefId = @RefId;
END;
GO

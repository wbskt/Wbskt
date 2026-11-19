CREATE PROCEDURE dbo.Bookmark_GetAllBy_MatchKeys
    @MatchKeysJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- The branch's id too, so a wake with no payload dispatches without reading the branch.
    SELECT
        b.Id,
        b.RefId,
        b.RunId,
        b.BranchRefId,
        br.Id AS BranchId,
        b.NodeId,
        b.WakeConditionKind,
        b.MatchKey,
        b.WakeConditionJson,
        b.ExpiresAt,
        b.TtlPort,
        b.CreatedAt
    FROM dbo.Bookmarks b
    LEFT JOIN dbo.Branches br ON br.RefId = b.BranchRefId
    WHERE b.MatchKey IN (
        SELECT [value]
        FROM OPENJSON(@MatchKeysJson)
    );
END;
GO

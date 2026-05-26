CREATE PROCEDURE dbo.Bookmark_GetDue
    @Now       DATETIME2(3),
    @BatchSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@BatchSize)
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
    FROM dbo.Bookmarks WITH (READPAST, UPDLOCK)
    WHERE ExpiresAt <= @Now
    ORDER BY ExpiresAt;
END;
GO

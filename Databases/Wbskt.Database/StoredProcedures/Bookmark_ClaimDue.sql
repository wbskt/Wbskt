CREATE PROCEDURE dbo.Bookmark_ClaimDue
    @Now       DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Claim-by-delete is atomic and needs no lease columns/host id: once a row is deleted here it
    -- cannot be claimed again. Accepted trade-off: a crash between this claim and dispatch loses
    -- the wake, but RunReaper (Run_GetStuck) now detects exactly that state (no bookmarks, no
    -- active branches) and reaps the run instead of leaving it wedged forever.
    DELETE TOP (@BatchSize) FROM dbo.Bookmarks WITH (ROWLOCK, READPAST)
    OUTPUT deleted.Id, deleted.RefId, deleted.RunId, deleted.BranchRefId, deleted.NodeId,
           deleted.WakeConditionKind, deleted.MatchKey, deleted.WakeConditionJson,
           deleted.ExpiresAt, deleted.TtlPort, deleted.CreatedAt
    WHERE ExpiresAt <= @Now;
END;
GO

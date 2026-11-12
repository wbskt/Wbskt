CREATE PROCEDURE dbo.Bookmark_ClaimDue
    @Now       DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Claim-by-delete is atomic and needs no lease columns/host id: once a row is deleted here it
    -- cannot be claimed again. A stop between this claim and dispatch loses the in-memory wake; the
    -- branch is left Waiting with no bookmark, and the next leader's RunRecoveryService resumes it
    -- (Branch_GetWaitingWithoutBookmark), where the parking node picks up from its persisted state.
    DELETE TOP (@BatchSize) FROM dbo.Bookmarks WITH (ROWLOCK, READPAST)
    OUTPUT deleted.Id, deleted.RefId, deleted.RunId, deleted.BranchRefId, deleted.NodeId,
           deleted.WakeConditionKind, deleted.MatchKey, deleted.WakeConditionJson,
           deleted.ExpiresAt, deleted.TtlPort, deleted.CreatedAt
    WHERE ExpiresAt <= @Now;
END;
GO

CREATE PROCEDURE dbo.Bookmark_ClaimByRefId
    @RefId     UNIQUEIDENTIFIER,
    @LocalJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Atomic claim-by-delete: whoever's DELETE actually removes the row wins the race between a
    -- signal/http/child-run match and a concurrent TTL expiry on the same bookmark. The loser's
    -- DELETE affects 0 rows and returns nothing, so it drops the event/resume as idempotent.
    --
    -- The winner stores its wake on the branch in the same transaction: WokenAt, and @LocalJson (the
    -- branch's local state with the wake payload merged in) when there is a payload. A stop before the
    -- dispatch then leaves a branch that recovery resumes with its payload (Branch_GetWoken), rather
    -- than one woken with nothing to say what woke it. The loser writes nothing.
    DECLARE @Claimed TABLE (Id BIGINT NOT NULL, BranchRefId UNIQUEIDENTIFIER NOT NULL);

    BEGIN TRANSACTION;

    DELETE FROM dbo.Bookmarks WITH (ROWLOCK)
    OUTPUT deleted.Id, deleted.BranchRefId INTO @Claimed
    WHERE RefId = @RefId;

    UPDATE br
    SET
        WokenAt = SYSUTCDATETIME(),
        UpdatedAt = SYSUTCDATETIME(),
        LocalJson = COALESCE(@LocalJson, br.LocalJson)
    FROM dbo.Branches br WITH (ROWLOCK)
    INNER JOIN @Claimed c ON c.BranchRefId = br.RefId
    WHERE br.Status = N'Waiting';

    COMMIT TRANSACTION;

    SELECT Id FROM @Claimed;
END;
GO

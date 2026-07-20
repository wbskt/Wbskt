CREATE PROCEDURE dbo.Bookmark_ClaimByRefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Atomic claim-by-delete: whoever's DELETE actually removes the row wins the race between a
    -- signal/http/child-run match and a concurrent TTL expiry on the same bookmark. The loser's
    -- DELETE affects 0 rows and returns nothing, so it drops the event/resume as idempotent.
    DELETE FROM dbo.Bookmarks WITH (ROWLOCK)
    OUTPUT deleted.Id
    WHERE RefId = @RefId;
END;
GO

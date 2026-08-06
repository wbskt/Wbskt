CREATE PROCEDURE dbo.Bookmark_DeleteOrphans
AS
BEGIN
    -- Must list EVERY terminal run status. A status missing here leaves its bookmarks behind
    -- forever, and the scheduler keeps claiming them to resume branches of a dead run.
    DELETE TOP (1000) b
    FROM dbo.Bookmarks b
    INNER JOIN dbo.Runs r ON r.Id = b.RunId
    WHERE r.Status IN ('Succeeded', 'Failed', 'Cancelled', 'PartiallyFailed', 'Faulted', 'OutOfCredits');
END;
GO

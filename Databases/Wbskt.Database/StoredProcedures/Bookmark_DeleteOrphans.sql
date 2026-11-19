CREATE PROCEDURE dbo.Bookmark_DeleteOrphans
AS
BEGIN
    -- No SET NOCOUNT ON: the caller returns the rows-affected count from ExecuteNonQuery.
    -- A bookmark of any run that is no longer in flight is an orphan. Written against the in-flight
    -- list, the one list every index filter keeps, so a new terminal status cannot leave its
    -- bookmarks behind for the scheduler to keep claiming.
    DELETE TOP (1000) b
    FROM dbo.Bookmarks b
    INNER JOIN dbo.Runs r ON r.Id = b.RunId
    WHERE r.Status NOT IN (N'Running', N'Failing', N'Cancelling');
END;
GO

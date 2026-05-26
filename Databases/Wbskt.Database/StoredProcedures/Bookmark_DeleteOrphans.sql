CREATE PROCEDURE dbo.Bookmark_DeleteOrphans
AS
BEGIN
    SET NOCOUNT ON;

    DELETE TOP (1000) b
    FROM dbo.Bookmarks b
    INNER JOIN dbo.Runs r ON r.Id = b.RunId
    WHERE r.Status IN ('Succeeded', 'Failed', 'Cancelled', 'PartiallyFailed');
END;
GO

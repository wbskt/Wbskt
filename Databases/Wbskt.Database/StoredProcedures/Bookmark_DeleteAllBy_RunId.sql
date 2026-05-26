CREATE PROCEDURE dbo.Bookmark_DeleteAllBy_RunId
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.Bookmarks
    WHERE RunId = @RunId;
END;
GO

CREATE PROCEDURE dbo.Bookmark_Delete
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.Bookmarks
    WHERE RefId = @RefId;
END;
GO

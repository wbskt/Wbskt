CREATE PROCEDURE dbo.Bookmark_Count
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT_BIG(*)
    FROM dbo.Bookmarks;
END;
GO

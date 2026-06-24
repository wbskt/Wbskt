CREATE PROCEDURE dbo.Bookmark_DeleteAllBy_RunId
    @RunId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.Bookmarks WITH (ROWLOCK)
    WHERE RunId = @RunId;
END;
GO

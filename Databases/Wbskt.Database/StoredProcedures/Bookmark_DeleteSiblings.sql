CREATE PROCEDURE dbo.Bookmark_DeleteSiblings
    @RunId INT,
    @BranchId INT,
    @ExcludeId INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.Bookmarks
    WHERE RunId = @RunId
      AND BranchRefId = (
            SELECT RefId
            FROM dbo.Branches
            WHERE Id = @BranchId)
      AND Id <> @ExcludeId;
END;
GO

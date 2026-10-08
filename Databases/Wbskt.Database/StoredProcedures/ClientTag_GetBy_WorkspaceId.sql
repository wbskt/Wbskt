-- Every tag in use in a workspace, with how many clients carry it, for the console's tag filter.
CREATE PROCEDURE dbo.ClientTag_GetBy_WorkspaceId
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        t.Tag,
        COUNT(*) AS ClientCount
    FROM dbo.ClientTags t
    INNER JOIN dbo.Clients c ON c.Id = t.ClientId
    WHERE c.WorkspaceId = @WorkspaceId
    GROUP BY t.Tag
    ORDER BY t.Tag;
END
GO

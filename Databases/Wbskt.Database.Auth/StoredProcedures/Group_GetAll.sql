CREATE PROCEDURE dbo.Group_GetAll
    @TenantId INT,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Groups
    WHERE TenantId = @TenantId;

    SELECT
        g.Id,
        g.RefId,
        g.Name,
        g.ParentGroupId,
        p.RefId AS ParentGroupRefId
    FROM dbo.Groups g
    LEFT JOIN dbo.Groups p ON p.Id = g.ParentGroupId
    WHERE g.TenantId = @TenantId
    ORDER BY g.Name
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

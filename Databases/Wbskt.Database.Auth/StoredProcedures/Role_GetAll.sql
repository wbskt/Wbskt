CREATE PROCEDURE dbo.Role_GetAll
    @TenantId INT,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Roles
    WHERE TenantId = @TenantId;

    SELECT
        Id,
        RefId,
        Name,
        Description
    FROM dbo.Roles
    WHERE TenantId = @TenantId
    ORDER BY Name
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

CREATE PROCEDURE dbo.Group_GetAll
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        ParentGroupId
    FROM dbo.Groups
    WHERE TenantId = @TenantId;
END
GO

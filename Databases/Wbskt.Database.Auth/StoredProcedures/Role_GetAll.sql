CREATE PROCEDURE dbo.Role_GetAll
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        Description
    FROM dbo.Roles
    WHERE TenantId = @TenantId;
END
GO

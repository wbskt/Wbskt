-- Tenant-scoped for the same reason as dbo.Role_FindBy_RefId.
CREATE PROCEDURE dbo.Group_FindBy_RefId
    @RefId UNIQUEIDENTIFIER,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Groups
    WHERE RefId = @RefId
      AND TenantId = @TenantId;
END
GO

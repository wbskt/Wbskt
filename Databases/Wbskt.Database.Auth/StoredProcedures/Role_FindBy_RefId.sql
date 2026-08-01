-- Scoped by tenant on purpose: a role reference from another tenant simply does not resolve,
-- so a caller cannot probe for or act on roles outside the tenant they were authorised against.
CREATE PROCEDURE dbo.Role_FindBy_RefId
    @RefId UNIQUEIDENTIFIER,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Roles
    WHERE RefId = @RefId
      AND TenantId = @TenantId;
END
GO

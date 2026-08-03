-- Renames a tenant or changes its description. This is the step that turns the tenant a user was
-- given at sign-up into a named organisation, so it is the only mutation a single-member tenant
-- normally needs.
CREATE PROCEDURE dbo.Tenant_Update
    @TenantId INT,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Tenants
    SET Name = @Name,
        Description = @Description
    WHERE Id = @TenantId;
END
GO

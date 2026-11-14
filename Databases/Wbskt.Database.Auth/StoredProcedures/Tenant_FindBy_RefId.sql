-- Resolves a tenant reference for the platform admin host's staff actions, which act on any tenant.
-- Every caller acting for a user resolves through dbo.Tenant_FindBy_RefIdForUser instead.
CREATE PROCEDURE dbo.Tenant_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Tenants
    WHERE RefId = @RefId;
END
GO

-- Resolves a workspace reference only within the tenant, so another tenant's workspace reads the
-- same as one that does not exist when it is named in an assignment or an invitation.
CREATE PROCEDURE dbo.Workspace_FindBy_RefIdInTenant
    @RefId UNIQUEIDENTIFIER,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Workspaces
    WHERE RefId = @RefId
      AND TenantId = @TenantId;
END
GO

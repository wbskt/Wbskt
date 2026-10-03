-- Resolves a tenant reference only if the user is a member of it. Non-membership and a bad
-- reference are indistinguishable to the caller, which is what stops tenant enumeration.
CREATE PROCEDURE dbo.Tenant_FindBy_RefIdForUser
    @RefId UNIQUEIDENTIFIER,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT T.Id
    FROM dbo.Tenants T
    INNER JOIN dbo.TenantMembers TM ON TM.TenantId = T.Id
    WHERE T.RefId = @RefId
      AND TM.UserId = @UserId
      AND TM.IsSuspended = 0; -- a suspended member is answered exactly like a non-member
END
GO

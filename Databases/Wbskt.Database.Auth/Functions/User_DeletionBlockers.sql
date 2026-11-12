-- The tenants that stop @UserId deleting their account: ones others belong to that nobody else
-- administers (dbo.Tenant_Administrators). dbo.User_Delete refuses (THROW 50018) while any is left,
-- and dbo.User_GetDeletionBlockers lets the caller ask first, before cancelling anything with the
-- payment provider.
CREATE FUNCTION dbo.User_DeletionBlockers (@UserId INT)
RETURNS TABLE
AS
RETURN
    SELECT TN.Id AS TenantId,
           TN.Name
    FROM dbo.TenantMembers TM
    INNER JOIN dbo.Tenants TN ON TN.Id = TM.TenantId
    WHERE TM.UserId = @UserId
      AND EXISTS (SELECT 1 FROM dbo.TenantMembers O WHERE O.TenantId = TM.TenantId AND O.UserId <> @UserId)
      AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(TM.TenantId) A WHERE A.UserId <> @UserId);
GO

-- Deletes a role and everything hanging off it. The assignment rows are removed explicitly rather
-- than by cascade so that deleting a role can never silently orphan a permission grant.
CREATE PROCEDURE dbo.Role_Delete
    @Id INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        THROW 50001, 'Role does not belong to the specified tenant.', 1;
    END

    BEGIN TRANSACTION;

    DELETE FROM dbo.RolePermissions WHERE RoleId = @Id;
    DELETE FROM dbo.UserRoles WHERE RoleId = @Id;
    DELETE FROM dbo.GroupRoles WHERE RoleId = @Id;

    -- Pending invitations that would have granted this role stay valid and simply grant nothing.
    -- Deleting them instead would revoke an outstanding invitation as a side effect of a role
    -- change, which the administrator did not ask for and would not see.
    UPDATE dbo.TenantInvitations SET RoleId = NULL WHERE RoleId = @Id;

    DELETE FROM dbo.Roles WHERE Id = @Id;

    COMMIT TRANSACTION;
END
GO

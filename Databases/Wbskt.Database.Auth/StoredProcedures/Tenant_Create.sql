-- Creates a tenant and everything required for it to be administrable, in one transaction.
--
-- A tenant is useless without all four parts: the row itself, its own roles (Roles are tenant-scoped
-- via UQ_Roles_Tenant_Name, so a new tenant starts with none and Workspace_Create's Admin lookup
-- would silently find nothing), a membership row for the creator, and a tenant-wide Admin
-- assignment. Splitting these across calls leaves a tenant nobody can administer, which no endpoint
-- can repair -- every management endpoint requires tenant-wide users.manage in the tenant to act.
--
-- The default workspace is created here rather than by a follow-up call to Workspace_Create for the
-- same reason: registration is the main caller, and a user whose tenant was created but whose
-- workspace was not cannot re-register (their email is taken) and has nothing to work in. Only the
-- two INSERTs are duplicated from Workspace_Create -- the owner already holds Admin tenant-wide, so
-- the workspace-scoped role grant that procedure performs would be redundant here.
CREATE PROCEDURE dbo.Tenant_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @OwnerUserId INT,
    @WorkspaceName NVARCHAR(100),
    @RefId UNIQUEIDENTIFIER OUTPUT,
    @TenantId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @OwnerUserId)
    BEGIN
        THROW 50010, 'Owner user does not exist.', 1;
    END

    SET @RefId = NEWID();

    BEGIN TRANSACTION;

    INSERT INTO dbo.Tenants (RefId, Name, Description)
    VALUES (@RefId, @Name, @Description);

    SET @TenantId = SCOPE_IDENTITY();

    -- Seeded per tenant, mirroring the post-deployment seed for the default tenant: Admin holds
    -- every permission in the catalogue, User holds the read permissions for a workspace's
    -- resources. An empty User role made inviting someone with it grant nothing at all; reads are
    -- the least a member needs to see the workspace they were added to, and the tenant's
    -- administrator can still narrow or widen the role.
    INSERT INTO dbo.Roles (TenantId, Name, Description, Kind)
    VALUES (@TenantId, 'Admin', 'Administrator with full access', 'Admin'),
           (@TenantId, 'User', 'Standard user', 'User');

    DECLARE @AdminRoleId INT = (SELECT Id FROM dbo.Roles WHERE TenantId = @TenantId AND Kind = 'Admin');

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, IsDeny)
    SELECT @AdminRoleId, P.Id, 0
    FROM dbo.Permissions P;

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, IsDeny)
    SELECT R.Id, P.Id, 0
    FROM dbo.Roles R
    INNER JOIN dbo.Permissions P ON P.Slug IN ('clients.read', 'policies.read', 'templates.read', 'workflows.read', 'logs.read')
    WHERE R.TenantId = @TenantId AND R.Kind = 'User';

    INSERT INTO dbo.TenantMembers (TenantId, UserId)
    VALUES (@TenantId, @OwnerUserId);

    -- WorkspaceId NULL = tenant-wide. This is the assignment the management API gates on; a
    -- workspace-scoped grant deliberately does not satisfy it.
    INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
    VALUES (@OwnerUserId, @AdminRoleId, @TenantId, NULL);

    INSERT INTO dbo.Workspaces (RefId, TenantId, Name, Description, OwnerUserId)
    VALUES (NEWID(), @TenantId, @WorkspaceName, 'Default workspace.', @OwnerUserId);

    -- Captured explicitly rather than calling SCOPE_IDENTITY() inline: every INSERT above targets a
    -- table with an identity column, so an inline call is correct only while it stays the statement
    -- immediately after the one it refers to.
    DECLARE @WorkspaceId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
    VALUES (@WorkspaceId, @OwnerUserId);

    COMMIT TRANSACTION;
END
GO

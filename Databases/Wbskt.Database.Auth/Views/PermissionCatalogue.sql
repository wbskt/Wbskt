-- The permission catalogue: every slug the hosts check, with its description, and whether it is one
-- of the reads every tenant's User role starts with. The one place either is listed. It must equal
-- PermissionNames (PermissionCatalogueTests).
--
-- The post-deployment script writes it into dbo.Permissions and grants a new default slug once to
-- the User roles that already exist; Tenant_Create grants the defaults to a new tenant's User role.
-- Not defaults: the manage permissions, users.read and roles.read, clients.firmware (a firmware image
-- replaces the code a client runs), billing.manage (it decides what the account spends), and
-- workspaces.create and .delete (tenant-wide, and a workspace counts against the plan).
CREATE VIEW dbo.PermissionCatalogue
AS
SELECT
    CONVERT(NVARCHAR(100), Slug) AS Slug,
    CONVERT(BIT, IsUserDefault) AS IsUserDefault,
    CONVERT(NVARCHAR(255), Description) AS Description
FROM (VALUES
    (N'users.read',          0, N'Read users, groups and tenant membership'),
    (N'users.manage',        0, N'Manage users'),
    (N'roles.read',          0, N'Read roles, permissions and assignments'),
    (N'roles.manage',        0, N'Manage roles and permissions'),
    (N'policies.read',       1, N'Read registration policies'),
    (N'policies.manage',     0, N'Manage registration policies'),
    (N'clients.read',        1, N'Read clients'),
    (N'clients.update',      0, N'Update client status'),
    (N'clients.manage',      0, N'Manage clients'),
    (N'clients.command',     0, N'Send commands to clients'),
    (N'clients.ping',        0, N'Ping clients'),
    (N'clients.firmware',    0, N'Update client firmware over the air'),
    (N'templates.read',      1, N'Read message templates'),
    (N'templates.manage',    0, N'Manage message templates'),
    (N'workflows.read',      1, N'Read workflows'),
    (N'workflows.create',    0, N'Create workflows'),
    (N'workflows.update',    0, N'Update workflows'),
    (N'workflows.delete',    0, N'Delete workflows'),
    (N'workflows.execute',   0, N'Start, signal and cancel workflow runs'),
    (N'workspace.join',      1, N'Join workspaces'),
    (N'workspaces.create',   0, N'Create workspaces in the tenant'),
    (N'workspaces.delete',   0, N'Delete any workspace in the tenant'),
    (N'logs.read',           1, N'Read event logs'),
    (N'integrations.read',   1, N'Read integrations'),
    (N'integrations.manage', 0, N'Add, change, test and delete integrations'),
    (N'billing.read',        1, N'Read the plan, its limits and what has been used'),
    (N'billing.manage',      0, N'Set a workspace''s monthly credit cap')
) AS Catalogue (Slug, IsUserDefault, Description);
GO

-- A pending offer of tenant membership. This is the only route into a tenant other than creating
-- one, which is what makes membership consensual: the invitee presents a token they were sent,
-- rather than an administrator naming an address and having the account joined without its owner.
--
-- TokenHash, not the token. The raw value is returned once at creation and never stored, so a read
-- of this table does not hand out tenant access -- the same reasoning applied to any credential.
CREATE TABLE dbo.TenantInvitations (
    Id INT IDENTITY(1, 1) NOT NULL,
    RefId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_TenantInvitations_RefId DEFAULT NEWID(),
    TenantId INT NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    RoleId INT NULL,
    TokenHash VARBINARY(32) NOT NULL,
    ExpiresAt DATETIME2(3) NOT NULL,
    AcceptedAt DATETIME2(3) NULL,
    AcceptedByUserId INT NULL,
    RevokedAt DATETIME2(3) NULL,
    InvitedByUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TenantInvitations PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_TenantInvitations_RefId UNIQUE (RefId),
    CONSTRAINT UQ_TenantInvitations_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_TenantInvitations_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE,

    -- No cascade: deleting a role must not silently erase the invitations that referenced it.
    -- Role_Delete clears the reference instead, leaving the invitation valid with no role attached.
    CONSTRAINT FK_TenantInvitations_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id),
    CONSTRAINT FK_TenantInvitations_InvitedBy FOREIGN KEY (InvitedByUserId) REFERENCES dbo.Users (Id),
    CONSTRAINT FK_TenantInvitations_AcceptedBy FOREIGN KEY (AcceptedByUserId) REFERENCES dbo.Users (Id)
);
GO

-- One live invitation per address per tenant. Filtered so that a revoked or accepted invitation
-- does not block re-inviting someone later -- the history stays queryable.
CREATE UNIQUE INDEX UX_TenantInvitations_Pending
    ON dbo.TenantInvitations (TenantId, Email)
    WHERE AcceptedAt IS NULL AND RevokedAt IS NULL;
GO

-- Workspaces an invitation adds its invitee to on acceptance. Without these, joining a tenant grants
-- membership and maybe a tenant-wide role but no workspace, and access resolves through
-- WorkspaceMembers, so a new member sees nothing until someone adds them by hand.
--
-- Cascades from both sides: a revoked or deleted invitation takes its list with it, and a workspace
-- deleted before the invitation is accepted simply drops out of it.
CREATE TABLE dbo.TenantInvitationWorkspaces (
    InvitationId INT NOT NULL,
    WorkspaceId INT NOT NULL,
    CONSTRAINT PK_TenantInvitationWorkspaces PRIMARY KEY CLUSTERED (InvitationId, WorkspaceId),
    CONSTRAINT FK_TenantInvitationWorkspaces_Invitations FOREIGN KEY (InvitationId) REFERENCES dbo.TenantInvitations (Id) ON DELETE CASCADE,
    CONSTRAINT FK_TenantInvitationWorkspaces_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES dbo.Workspaces (Id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_TenantInvitationWorkspaces_WorkspaceId
    ON dbo.TenantInvitationWorkspaces (WorkspaceId);
GO

-- The workspaces an invitation joins its invitee to, by name. A workspace deleted since it was sent is
-- gone from the invitation too (the foreign key cascades), so it is not listed.
CREATE PROCEDURE dbo.TenantInvitationWorkspace_GetBy_InvitationRefId
    @InvitationRefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT W.RefId,
           W.Name
    FROM dbo.TenantInvitations I
    INNER JOIN dbo.TenantInvitationWorkspaces IW ON IW.InvitationId = I.Id
    INNER JOIN dbo.Workspaces W ON W.Id = IW.WorkspaceId AND W.TenantId = I.TenantId
    WHERE I.RefId = @InvitationRefId
    ORDER BY W.Name;
END
GO

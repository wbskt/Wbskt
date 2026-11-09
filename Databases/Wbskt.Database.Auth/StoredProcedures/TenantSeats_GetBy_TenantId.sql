-- The tenant's workspaces and members, counted as Workspace_Create and TenantInvitation_Accept count
-- them against the plan.
CREATE PROCEDURE dbo.TenantSeats_GetBy_TenantId
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        (SELECT COUNT(*) FROM dbo.Workspaces WHERE TenantId = @TenantId) AS Workspaces,
        (SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @TenantId) AS Members;
END
GO

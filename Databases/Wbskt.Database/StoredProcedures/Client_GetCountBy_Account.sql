-- The registered clients of @WorkspaceId's account (dbo.TenantWorkspaces, as Client_Create counts them
-- against the plan's client limit), for the numbers a refusal at that limit reports.
CREATE PROCEDURE dbo.Client_GetCountBy_Account
    @WorkspaceId INT,
    @TenantId    INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*)
    FROM dbo.Clients c
    JOIN dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w ON w.WorkspaceId = c.WorkspaceId
    WHERE c.Status = 1;
END;
GO

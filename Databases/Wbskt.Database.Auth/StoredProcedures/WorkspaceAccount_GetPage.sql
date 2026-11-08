-- Each workspace with its tenant's plan, for the main database's copies (dbo.WorkspaceAccounts there),
-- ordered by workspace id and read a page at a time after @AfterWorkspaceId. @TenantId or @WorkspaceId
-- narrows it to one tenant's workspaces (after a plan change) or one workspace (after it is created).
CREATE PROCEDURE dbo.WorkspaceAccount_GetPage
    @AfterWorkspaceId INT = NULL,
    @Take             INT,
    @TenantId         INT = NULL,
    @WorkspaceId      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take)
        w.Id AS WorkspaceId,
        t.Id AS TenantId,
        t.RefId AS TenantRefId,
        t.PlanCode,
        t.PlanLimitOverrides,
        t.PlanRevision
    FROM dbo.Workspaces w
    JOIN dbo.Tenants t ON t.Id = w.TenantId
    WHERE (@AfterWorkspaceId IS NULL OR w.Id > @AfterWorkspaceId)
      AND (@TenantId IS NULL OR w.TenantId = @TenantId)
      AND (@WorkspaceId IS NULL OR w.Id = @WorkspaceId)
    ORDER BY w.Id;
END
GO

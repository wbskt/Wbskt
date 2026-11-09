-- What a workspace and its tenant (dbo.TenantWorkspaces, as the plan limits count them) have used of
-- the plan: workflow credits since @MonthStart (the ledger plus running runs' unposted credits, as
-- Run_Start counts them), client messages since @MonthStart (dbo.WorkspaceMessageUsage), registered
-- clients and runs going, and the workspace's own credit cap.
CREATE PROCEDURE dbo.WorkspaceUsage_GetSummary
    @WorkspaceId INT,
    @TenantId    INT,
    @MonthStart  DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Workspaces TABLE (WorkspaceId INT NOT NULL PRIMARY KEY);
    INSERT INTO @Workspaces (WorkspaceId)
    SELECT WorkspaceId FROM dbo.TenantWorkspaces(@WorkspaceId, @TenantId);

    DECLARE @Credits TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    INSERT INTO @Credits (WorkspaceId, Credits)
    SELECT w.WorkspaceId, s.Credits
    FROM @Workspaces w
    CROSS APPLY dbo.WorkspaceCreditsSpent(w.WorkspaceId, @MonthStart) s;

    DECLARE @Messages TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Messages BIGINT NOT NULL);
    INSERT INTO @Messages (WorkspaceId, Messages)
    SELECT m.WorkspaceId, SUM(m.Inbound + m.Outbound)
    FROM dbo.WorkspaceMessageUsage m
    JOIN @Workspaces w ON w.WorkspaceId = m.WorkspaceId
    WHERE m.Day >= @MonthStart
    GROUP BY m.WorkspaceId;

    DECLARE @Clients TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Clients INT NOT NULL);
    INSERT INTO @Clients (WorkspaceId, Clients)
    SELECT c.WorkspaceId, COUNT(*)
    FROM dbo.Clients c
    JOIN @Workspaces w ON w.WorkspaceId = c.WorkspaceId
    WHERE c.Status = 1
    GROUP BY c.WorkspaceId;

    -- The Status IN list is IX_Runs_WorkflowDefinitionId_Active's filter word for word.
    DECLARE @Runs TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Runs INT NOT NULL);
    INSERT INTO @Runs (WorkspaceId, Runs)
    SELECT d.WorkspaceId, COUNT(*)
    FROM dbo.Runs r
    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
    JOIN @Workspaces w ON w.WorkspaceId = d.WorkspaceId
    WHERE r.Status IN (N'Running', N'Failing', N'Cancelling')
    GROUP BY d.WorkspaceId;

    SELECT
        (SELECT ISNULL(SUM(Credits), 0) FROM @Credits) AS TenantCredits,
        (SELECT ISNULL(SUM(Credits), 0) FROM @Credits WHERE WorkspaceId = @WorkspaceId) AS WorkspaceCredits,
        (SELECT ISNULL(SUM(Messages), 0) FROM @Messages) AS TenantMessages,
        (SELECT ISNULL(SUM(Messages), 0) FROM @Messages WHERE WorkspaceId = @WorkspaceId) AS WorkspaceMessages,
        (SELECT ISNULL(SUM(Clients), 0) FROM @Clients) AS TenantClients,
        (SELECT ISNULL(SUM(Clients), 0) FROM @Clients WHERE WorkspaceId = @WorkspaceId) AS WorkspaceClients,
        (SELECT ISNULL(SUM(Runs), 0) FROM @Runs) AS TenantActiveRuns,
        (SELECT ISNULL(SUM(Runs), 0) FROM @Runs WHERE WorkspaceId = @WorkspaceId) AS WorkspaceActiveRuns,
        (SELECT MonthlyCreditCap FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId) AS MonthlyCreditCap;
END;
GO

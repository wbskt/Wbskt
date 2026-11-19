-- What a workspace and its tenant (dbo.TenantWorkspaces, as the plan limits count them) have used of
-- the plan: workflow credits since @MonthStart (the ledger plus runs' unposted credits, ended or not, as
-- Run_Start counts them), client messages since @MonthStart (dbo.WorkspaceMessageUsage), registered
-- clients and runs going, the workspace's own credit cap and what its runs going hold of it, the credits
-- the tenant's grants still hold, and which of those expire first: when, and how many. RemainingCredits
-- is what new runs may still be given, worked out by dbo.TenantCreditsLeft as Run_Start works it out, with
-- the allowance read from the plan copy as RunCounters_TryCharge reads it (@Allowance, the caller's,
-- stands in for a workspace with no copy yet), never below zero.
CREATE PROCEDURE dbo.WorkspaceUsage_GetSummary
    @WorkspaceId INT,
    @TenantId    INT,
    @MonthStart  DATE,
    -- Optional, so a host before RemainingCredits calls it as before: where new runs are refused for the
    -- month's credits on the caller's plan, and the caller's clock (the SQL clock without it).
    @Allowance   DECIMAL(18,4) = NULL,
    @Now         DATETIME2(3) = NULL
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

    -- The live grant that expires first, as dbo.TenantCreditGrantBalance counts live; a grant drawn to
    -- nothing is left out, since nothing of it can expire.
    SET @Now = COALESCE(@Now, SYSUTCDATETIME());
    DECLARE @NextExpiresAt DATETIME2(3) = (
        SELECT MIN(g.ExpiresAt)
        FROM dbo.TenantCreditGrants g
        WHERE g.TenantId = @TenantId
          AND g.RevokedAt IS NULL
          AND g.HeldAt IS NULL
          AND g.ExpiresAt > @Now
          AND g.Credits > g.Drawn);

    DECLARE @RefusedAt DECIMAL(18,4) = COALESCE(
        (SELECT dbo.PlanCreditsRefusedAt(wa.PlanCode, wa.CreditsPerMonth)
         FROM dbo.WorkspaceAccounts wa
         WHERE wa.WorkspaceId = @WorkspaceId),
        @Allowance);

    SELECT
        (SELECT ISNULL(SUM(Credits), 0) FROM @Credits) AS TenantCredits,
        (SELECT ISNULL(SUM(Credits), 0) FROM @Credits WHERE WorkspaceId = @WorkspaceId) AS WorkspaceCredits,
        (SELECT ISNULL(SUM(Messages), 0) FROM @Messages) AS TenantMessages,
        (SELECT ISNULL(SUM(Messages), 0) FROM @Messages WHERE WorkspaceId = @WorkspaceId) AS WorkspaceMessages,
        (SELECT ISNULL(SUM(Clients), 0) FROM @Clients) AS TenantClients,
        (SELECT ISNULL(SUM(Clients), 0) FROM @Clients WHERE WorkspaceId = @WorkspaceId) AS WorkspaceClients,
        (SELECT ISNULL(SUM(Runs), 0) FROM @Runs) AS TenantActiveRuns,
        (SELECT ISNULL(SUM(Runs), 0) FROM @Runs WHERE WorkspaceId = @WorkspaceId) AS WorkspaceActiveRuns,
        (SELECT MonthlyCreditCap FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId) AS MonthlyCreditCap,
        (SELECT Credits FROM dbo.TenantCreditGrantBalance(@TenantId, @Now)) AS CreditGrantBalance,
        (SELECT Credits FROM dbo.WorkspaceCreditsReserved(@WorkspaceId)) AS WorkspaceCreditsReserved,
        @NextExpiresAt AS CreditGrantNextExpiresAt,
        (SELECT ISNULL(SUM(g.Credits - g.Drawn), 0)
         FROM dbo.TenantCreditGrants g
         WHERE g.TenantId = @TenantId
           AND g.RevokedAt IS NULL
           AND g.HeldAt IS NULL
           AND g.ExpiresAt = @NextExpiresAt) AS CreditGrantNextExpiring,
        (SELECT ISNULL(SUM(u.Credits), 0)
         FROM dbo.WorkspaceCreditUsage u
         JOIN @Workspaces w ON w.WorkspaceId = u.WorkspaceId
         WHERE u.Day >= @MonthStart) AS TenantCreditsPosted,
        -- TenantCreditsPosted, TenantCreditsDrawn and TenantCreditsReserved fed a copy of this sum in
        -- the host before RemainingCredits; kept so that host still reads its row, and dropped in the
        -- release after (#1059).
        ISNULL((SELECT Drawn
                FROM dbo.TenantCreditMonths
                WHERE TenantId = @TenantId
                  AND MonthStart = @MonthStart), 0) AS TenantCreditsDrawn,
        (SELECT Credits FROM dbo.TenantCreditsReserved(@WorkspaceId, @TenantId)) AS TenantCreditsReserved,
        (SELECT CASE WHEN l.Credits > 0 THEN l.Credits ELSE 0 END
         FROM dbo.TenantCreditsLeft(@WorkspaceId, @TenantId, @RefusedAt, @Now) l) AS RemainingCredits;
END;
GO

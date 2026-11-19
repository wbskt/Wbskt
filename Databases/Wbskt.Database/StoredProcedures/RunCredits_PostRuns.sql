-- Moves what each of @RunIds consumed since it last posted into dbo.WorkspaceCreditUsage on @Day, and
-- draws what each tenant posted past its plan's monthly allowance from its credit grants
-- (dbo.TenantCreditGrants_Draw) at @At. Returns a row per workspace that posted: how many runs and
-- credits it moved. The one ledger writer behind RunCredits_Post and Run_DeleteRetired, which call it
-- inside their own transaction (INSERT ... EXEC), so the two cannot drift apart.
-- A run's PostedCredits moves in the same statement that reads its delta, so a retry never posts twice.
-- Lock order: the runs' counters, then the ledger rows, then the tenants' month rows and grants.
CREATE PROCEDURE dbo.RunCredits_PostRuns
    @RunIds dbo.IdTableType READONLY,
    @Day    DATE,
    @At     DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Moved TABLE (RunId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    DECLARE @ByWorkspace TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Runs INT NOT NULL, Credits DECIMAL(18,4) NOT NULL);

    UPDATE rc
    SET PostedCredits = rc.CreditsConsumed,
        HasUnposted = 0
    OUTPUT inserted.RunId, inserted.PostedCredits - deleted.PostedCredits INTO @Moved (RunId, Credits)
    FROM dbo.RunCounters rc
    JOIN @RunIds i ON i.Id = rc.RunId
    WHERE rc.HasUnposted = 1
       OR rc.CreditsConsumed <> rc.PostedCredits;

    INSERT INTO @ByWorkspace (WorkspaceId, Runs, Credits)
    SELECT d.WorkspaceId, COUNT(*), SUM(m.Credits)
    FROM @Moved m
    JOIN dbo.Runs r ON r.Id = m.RunId
    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
    WHERE m.Credits <> 0
    GROUP BY d.WorkspaceId;

    MERGE dbo.WorkspaceCreditUsage WITH (HOLDLOCK) AS t
    USING @ByWorkspace AS s
    ON t.WorkspaceId = s.WorkspaceId AND t.Day = @Day
    WHEN MATCHED THEN
        UPDATE SET Credits = t.Credits + s.Credits, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Credits) VALUES (s.WorkspaceId, @Day, s.Credits);

    -- What each tenant posted past its plan's monthly allowance is drawn from its credit grants now,
    -- under the allowance it has now. A workspace with no copy of its account has no tenant to draw for.
    DECLARE @Posts dbo.TenantCreditPostTableType;
    INSERT INTO @Posts (TenantId, Credits, Allowance)
    SELECT a.TenantId, SUM(w.Credits), MAX(a.CreditsPerMonth)
    FROM @ByWorkspace w
    JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = w.WorkspaceId
    WHERE a.CreditsPerMonth IS NOT NULL
    GROUP BY a.TenantId;

    -- A deleted workspace's copy (RetiredAt) keeps the allowance it last had, so a cancelled run of it
    -- posting late is held to the tenant's allowance from a live copy when there is one.
    UPDATE p
    SET Allowance = l.Allowance
    FROM @Posts p
    CROSS APPLY (SELECT MAX(a.CreditsPerMonth) AS Allowance
                 FROM dbo.WorkspaceAccounts a
                 WHERE a.TenantId = p.TenantId
                   AND a.RetiredAt IS NULL
                   AND a.CreditsPerMonth IS NOT NULL) l
    WHERE l.Allowance IS NOT NULL;

    IF EXISTS (SELECT 1 FROM @Posts)
    BEGIN
        DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Day), MONTH(@Day), 1);
        EXEC dbo.TenantCreditGrants_Draw @Posts = @Posts, @MonthStart = @MonthStart, @At = @At;
    END

    SELECT WorkspaceId, Runs, Credits
    FROM @ByWorkspace;
END;
GO

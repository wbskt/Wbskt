-- Moves the credits runs consumed since they last posted into dbo.WorkspaceCreditUsage, on @Day, for
-- up to @BatchSize runs. Returns a row per workspace that posted: how many runs and credits it moved,
-- what its ledger now holds for @Day's month, and its credit cap (dbo.WorkspaceCreditCaps, NULL for
-- none), so the caller can tell which caps it just carried a workspace across; and its tenant, the plan's
-- monthly allowance and what the tenant has posted this month with it (dbo.TenantCreditMonths), so the
-- caller can tell when the tenant crossed its allowance. Credits Run_DeleteRetired posts are not seen
-- here, so a crossing made only by them is not reported. Off the hot path: RunCounters_TryCharge only
-- adds to a run's own counter, and the engine calls this every few seconds (CreditUsagePoster).
-- Reads every counter marked as holding unposted credits (HasUnposted, set by each charge and the start
-- cost), however long ago its run ended, through IX_RunCounters_Unposted. A run's
-- PostedCredits moves in the same statement that reads its delta, so a retry never posts twice.
-- What a tenant posts past its plan's allowance is drawn from its credit grants in the same transaction
-- (dbo.TenantCreditGrants_Draw).
-- Lock order: the runs' counters, then the ledger rows, then the tenants' month rows and grants (as in
-- Run_DeleteRetired).
CREATE PROCEDURE dbo.RunCredits_Post
    @Day        DATE,
    @BatchSize  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Candidates TABLE (RunId INT NOT NULL PRIMARY KEY, WorkspaceId INT NOT NULL);
    DECLARE @Moved TABLE (RunId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    DECLARE @ByWorkspace TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Runs INT NOT NULL, Credits DECIMAL(18,4) NOT NULL);

    INSERT INTO @Candidates (RunId, WorkspaceId)
    SELECT TOP (@BatchSize)
        rc.RunId,
        d.WorkspaceId
    FROM dbo.RunCounters rc
    JOIN dbo.Runs r ON r.Id = rc.RunId
    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
    WHERE rc.HasUnposted = 1;

    BEGIN TRANSACTION;

    UPDATE rc
    SET PostedCredits = rc.CreditsConsumed,
        HasUnposted = 0
    OUTPUT inserted.RunId, inserted.PostedCredits - deleted.PostedCredits INTO @Moved (RunId, Credits)
    FROM dbo.RunCounters rc
    JOIN @Candidates c ON c.RunId = rc.RunId
    WHERE rc.HasUnposted = 1;

    INSERT INTO @ByWorkspace (WorkspaceId, Runs, Credits)
    SELECT c.WorkspaceId, COUNT(*), SUM(m.Credits)
    FROM @Moved m
    JOIN @Candidates c ON c.RunId = m.RunId
    WHERE m.Credits <> 0
    GROUP BY c.WorkspaceId;

    MERGE dbo.WorkspaceCreditUsage WITH (HOLDLOCK) AS t
    USING @ByWorkspace AS s
    ON t.WorkspaceId = s.WorkspaceId AND t.Day = @Day
    WHEN MATCHED THEN
        UPDATE SET Credits = t.Credits + s.Credits, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Credits) VALUES (s.WorkspaceId, @Day, s.Credits);

    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Day), MONTH(@Day), 1);

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

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    IF EXISTS (SELECT 1 FROM @Posts)
        EXEC dbo.TenantCreditGrants_Draw @Posts = @Posts, @MonthStart = @MonthStart, @At = @Now;

    -- Read before the commit, while the ledger rows just written are still held, so two posts that
    -- meet on a workspace each see their own step across a cap.

    SELECT
        w.WorkspaceId,
        w.Runs,
        w.Credits,
        (SELECT SUM(u.Credits)
         FROM dbo.WorkspaceCreditUsage u
         WHERE u.WorkspaceId = w.WorkspaceId
           AND u.Day >= @MonthStart) AS SpentThisMonth,
        c.MonthlyCreditCap,
        a.TenantId,
        a.CreditsPerMonth,
        m.Posted AS TenantSpentThisMonth
    FROM @ByWorkspace w
    LEFT JOIN dbo.WorkspaceCreditCaps c ON c.WorkspaceId = w.WorkspaceId
    LEFT JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = w.WorkspaceId
    LEFT JOIN dbo.TenantCreditMonths m ON m.TenantId = a.TenantId AND m.MonthStart = @MonthStart;

    COMMIT TRANSACTION;
END;
GO

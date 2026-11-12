-- Deletes up to @BatchSize finished runs, with everything that hangs off them, and the test copies of
-- workflows that no run points at any more. Returns how many runs went.
-- A run goes once it finished before its workspace's plan cutoff: @NowUtc less the plan's days in
-- @Retention, or the tenant's runHistoryDays override, and never sooner than a retention still held
-- after a downgrade (dbo.WorkspaceAccounts.RetentionHeldUntil). A workspace with no copy of its account
-- yet has no plan cutoff: its plan is not known. A run finished before @CutoffUtc goes whatever the plan
-- says, so no run outlives HistoryRetentionGc's elevated window.
CREATE PROCEDURE dbo.Run_DeleteRetired
    @NowUtc    DATETIME2(3),
    @Retention dbo.RunHistoryRetentionTableType READONLY,
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    CREATE TABLE #Cutoffs (
        WorkspaceId INT          NOT NULL PRIMARY KEY,
        CutoffUtc   DATETIME2(3) NOT NULL
    );

    INSERT INTO #Cutoffs (WorkspaceId, CutoffUtc)
    SELECT
        a.WorkspaceId,
        DATEADD(DAY, -CASE WHEN a.RetentionHeldUntil > @NowUtc AND a.HeldRunHistoryDays > d.Days THEN a.HeldRunHistoryDays ELSE d.Days END, @NowUtc)
    FROM dbo.WorkspaceAccounts a
    JOIN @Retention p ON p.PlanCode = a.PlanCode
    CROSS APPLY (SELECT COALESCE(TRY_CAST(JSON_VALUE(a.LimitOverrides, '$.runHistoryDays') AS INT), p.Days) AS Days) d
    WHERE EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions wd WHERE wd.WorkspaceId = a.WorkspaceId);

    DECLARE @Runs TABLE (Id INT NOT NULL PRIMARY KEY);

    -- A seek per workflow on IX_Runs_WorkflowDefinitionId_CompletedAt, up to its own cutoff: the runs a
    -- longer plan still keeps are never read, where one scan of every finished run by CompletedAt passed
    -- over all of them on every batch.
    INSERT INTO @Runs (Id)
    SELECT TOP (@BatchSize) x.Id
    FROM dbo.WorkflowDefinitions d
    LEFT JOIN #Cutoffs c ON c.WorkspaceId = d.WorkspaceId
    CROSS APPLY (SELECT TOP (@BatchSize) r.Id, r.CompletedAt
                 FROM dbo.Runs r
                 WHERE r.WorkflowDefinitionId = d.Id
                   AND r.CompletedAt < CASE WHEN c.CutoffUtc > @CutoffUtc THEN c.CutoffUtc ELSE @CutoffUtc END
                 ORDER BY r.CompletedAt) x
    ORDER BY x.CompletedAt;

    BEGIN TRANSACTION;

    -- Credits a run consumed after its last post go to the ledger before its counter is deleted, so the
    -- ledger still adds up to what every run consumed. Same lock order as RunCredits_Post.
    DECLARE @Moved TABLE (RunId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);

    UPDATE rc
    SET PostedCredits = rc.CreditsConsumed,
        HasUnposted = 0
    OUTPUT inserted.RunId, inserted.PostedCredits - deleted.PostedCredits INTO @Moved (RunId, Credits)
    FROM dbo.RunCounters rc
    WHERE rc.RunId IN (SELECT Id FROM @Runs)
      AND rc.CreditsConsumed <> rc.PostedCredits;

    DECLARE @ByWorkspace TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    INSERT INTO @ByWorkspace (WorkspaceId, Credits)
    SELECT d.WorkspaceId, SUM(m.Credits)
    FROM @Moved m
    JOIN dbo.Runs r ON r.Id = m.RunId
    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
    GROUP BY d.WorkspaceId;

    MERGE dbo.WorkspaceCreditUsage WITH (HOLDLOCK) AS t
    USING @ByWorkspace AS s
    ON t.WorkspaceId = s.WorkspaceId AND t.Day = @Today
    WHEN MATCHED THEN
        UPDATE SET Credits = t.Credits + s.Credits, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Credits) VALUES (s.WorkspaceId, @Today, s.Credits);

    -- Drawn from the tenants' grants past their allowance, as RunCredits_Post does.
    DECLARE @Posts dbo.TenantCreditPostTableType;
    INSERT INTO @Posts (TenantId, Credits, Allowance)
    SELECT a.TenantId, SUM(w.Credits), MAX(a.CreditsPerMonth)
    FROM @ByWorkspace w
    JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = w.WorkspaceId
    WHERE a.CreditsPerMonth IS NOT NULL
    GROUP BY a.TenantId;

    IF EXISTS (SELECT 1 FROM @Posts)
    BEGIN
        DECLARE @TodayMonth DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
        DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
        EXEC dbo.TenantCreditGrants_Draw @Posts = @Posts, @MonthStart = @TodayMonth, @At = @Now;
    END

    DELETE FROM dbo.HistoryEvents  WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.Bookmarks      WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.IdempotencyKeys WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE C
    FROM dbo.JoinContributions C
    INNER JOIN dbo.JoinAggregators A ON A.JoinToken = C.JoinToken
    WHERE A.RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.JoinAggregators WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.RunCounters    WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.Branches       WHERE RunId IN (SELECT Id FROM @Runs);
    DELETE FROM dbo.Runs           WHERE Id IN (SELECT Id FROM @Runs);

    -- A test copy is kept only for the runs started from it. A day old at least, so a copy whose run is
    -- still being started is never taken, even on a plan overridden to keep no history.
    DELETE WD
    FROM dbo.WorkflowDefinitions WD
    LEFT JOIN #Cutoffs c ON c.WorkspaceId = WD.WorkspaceId
    WHERE WD.Kind = N'Test'
      AND WD.CreatedAt < DATEADD(DAY, -1, @NowUtc)
      AND (WD.CreatedAt < @CutoffUtc OR WD.CreatedAt < c.CutoffUtc)
      AND NOT EXISTS (SELECT 1 FROM dbo.Runs R WHERE R.WorkflowDefinitionId = WD.Id);

    COMMIT TRANSACTION;

    SELECT COUNT(*) FROM @Runs;
END;
GO

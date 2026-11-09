-- Moves the credits runs consumed since they last posted into dbo.WorkspaceCreditUsage, on @Day, for
-- up to @BatchSize runs. Returns a row per workspace that posted: how many runs and credits it moved,
-- what its ledger now holds for @Day's month, and its credit cap (dbo.WorkspaceCreditCaps, NULL for
-- none), so the caller can tell which caps it just carried a workspace across. Off the hot path: RunCounters_TryCharge only
-- adds to a run's own counter, and the engine calls this every few seconds (CreditUsagePoster).
-- Reads the runs still going and those that ended since @EndedSince, which catches a charge landing
-- just after a run ended; Run_DeleteRetired posts anything left before a run is deleted. A run's
-- PostedCredits moves in the same statement that reads its delta, so a retry never posts twice.
-- Lock order: the runs' counters, then the ledger rows (as in Run_DeleteRetired).
CREATE PROCEDURE dbo.RunCredits_Post
    @Day        DATE,
    @EndedSince DATETIME2(3),
    @BatchSize  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Candidates TABLE (RunId INT NOT NULL PRIMARY KEY, WorkspaceId INT NOT NULL);
    DECLARE @Moved TABLE (RunId INT NOT NULL PRIMARY KEY, Credits DECIMAL(18,4) NOT NULL);
    DECLARE @ByWorkspace TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Runs INT NOT NULL, Credits DECIMAL(18,4) NOT NULL);

    -- The Status IN list is IX_Runs_WorkflowDefinitionId_Active's filter word for word.
    INSERT INTO @Candidates (RunId, WorkspaceId)
    SELECT TOP (@BatchSize)
        c.RunId,
        d.WorkspaceId
    FROM (SELECT r.Id AS RunId, r.WorkflowDefinitionId
          FROM dbo.Runs r
          WHERE r.Status IN (N'Running', N'Failing', N'Cancelling')
          UNION
          SELECT r.Id, r.WorkflowDefinitionId
          FROM dbo.Runs r
          WHERE r.CompletedAt >= @EndedSince) c
    JOIN dbo.RunCounters rc ON rc.RunId = c.RunId
    JOIN dbo.WorkflowDefinitions d ON d.Id = c.WorkflowDefinitionId
    WHERE rc.CreditsConsumed <> rc.PostedCredits;

    BEGIN TRANSACTION;

    UPDATE rc
    SET PostedCredits = rc.CreditsConsumed
    OUTPUT inserted.RunId, inserted.PostedCredits - deleted.PostedCredits INTO @Moved (RunId, Credits)
    FROM dbo.RunCounters rc
    JOIN @Candidates c ON c.RunId = rc.RunId
    WHERE rc.CreditsConsumed <> rc.PostedCredits;

    INSERT INTO @ByWorkspace (WorkspaceId, Runs, Credits)
    SELECT c.WorkspaceId, COUNT(*), SUM(m.Credits)
    FROM @Moved m
    JOIN @Candidates c ON c.RunId = m.RunId
    GROUP BY c.WorkspaceId;

    MERGE dbo.WorkspaceCreditUsage WITH (HOLDLOCK) AS t
    USING @ByWorkspace AS s
    ON t.WorkspaceId = s.WorkspaceId AND t.Day = @Day
    WHEN MATCHED THEN
        UPDATE SET Credits = t.Credits + s.Credits, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Credits) VALUES (s.WorkspaceId, @Day, s.Credits);

    -- Read before the commit, while the ledger rows just written are still held, so two posts that
    -- meet on a workspace each see their own step across a cap.
    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Day), MONTH(@Day), 1);

    SELECT
        w.WorkspaceId,
        w.Runs,
        w.Credits,
        (SELECT SUM(u.Credits)
         FROM dbo.WorkspaceCreditUsage u
         WHERE u.WorkspaceId = w.WorkspaceId
           AND u.Day >= @MonthStart) AS SpentThisMonth,
        c.MonthlyCreditCap
    FROM @ByWorkspace w
    LEFT JOIN dbo.WorkspaceCreditCaps c ON c.WorkspaceId = w.WorkspaceId;

    COMMIT TRANSACTION;
END;
GO

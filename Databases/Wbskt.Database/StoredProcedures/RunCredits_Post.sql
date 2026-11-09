-- Moves the credits runs consumed since they last posted into dbo.WorkspaceCreditUsage, on @Day, for
-- up to @BatchSize runs. Returns how many runs posted. Off the hot path: RunCounters_TryCharge only
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

    MERGE dbo.WorkspaceCreditUsage WITH (HOLDLOCK) AS t
    USING (SELECT c.WorkspaceId, SUM(m.Credits) AS Credits
           FROM @Moved m
           JOIN @Candidates c ON c.RunId = m.RunId
           GROUP BY c.WorkspaceId) AS s
    ON t.WorkspaceId = s.WorkspaceId AND t.Day = @Day
    WHEN MATCHED THEN
        UPDATE SET Credits = t.Credits + s.Credits, UpdatedAt = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (WorkspaceId, Day, Credits) VALUES (s.WorkspaceId, @Day, s.Credits);

    COMMIT TRANSACTION;

    SELECT COUNT(*) FROM @Moved;
END;
GO

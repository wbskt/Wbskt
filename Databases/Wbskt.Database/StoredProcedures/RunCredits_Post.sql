-- Moves the credits runs consumed since they last posted into dbo.WorkspaceCreditUsage, on @Day, for
-- up to @BatchSize runs. Returns a row per workspace that posted: how many runs and credits it moved.
-- The 80% and 100% notices are worked out afterwards from what the ledger holds (CreditNotices_GetDue),
-- not from this post's step. Off the hot path: RunCounters_TryCharge only adds to a run's own counter,
-- and the engine calls this every few seconds (CreditUsagePoster).
-- Reads every counter marked as holding unposted credits (HasUnposted, set by each charge and the start
-- cost), however long ago its run ended, through IX_RunCounters_Unposted. The posting itself, and
-- the draw from credit grants past the allowance, is dbo.RunCredits_PostRuns, which Run_DeleteRetired
-- calls too.
CREATE PROCEDURE dbo.RunCredits_Post
    @Day        DATE,
    @BatchSize  INT,
    -- The engine's clock, for which credit grants have expired when what is posted past the allowance
    -- is drawn from them. NULL, from an engine before it passed one, reads the SQL clock; that fallback
    -- is dropped in the release after (#1059).
    @At         DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Candidates dbo.IdTableType;
    DECLARE @ByWorkspace TABLE (WorkspaceId INT NOT NULL PRIMARY KEY, Runs INT NOT NULL, Credits DECIMAL(18,4) NOT NULL);
    SET @At = COALESCE(@At, SYSUTCDATETIME());

    INSERT INTO @Candidates (Id)
    SELECT TOP (@BatchSize) rc.RunId
    FROM dbo.RunCounters rc
    WHERE rc.HasUnposted = 1;

    BEGIN TRANSACTION;

    INSERT INTO @ByWorkspace (WorkspaceId, Runs, Credits)
    EXEC dbo.RunCredits_PostRuns @RunIds = @Candidates, @Day = @Day, @At = @At;

    SELECT
        w.WorkspaceId,
        w.Runs,
        w.Credits
    FROM @ByWorkspace w;

    COMMIT TRANSACTION;
END;
GO

-- The workflow credits @WorkspaceId has spent since @MonthStart: what the ledger holds plus what its
-- runs still going consumed and have not posted yet (RunCredits_Post). What a workspace's credit cap is
-- held to (Run_Start) and reported against (WorkspaceCreditCap_Set).
CREATE FUNCTION dbo.WorkspaceCreditsSpent (@WorkspaceId INT, @MonthStart DATE)
RETURNS TABLE
AS
RETURN
(
    SELECT
        ISNULL((SELECT SUM(u.Credits)
                FROM dbo.WorkspaceCreditUsage u
                WHERE u.WorkspaceId = @WorkspaceId
                  AND u.Day >= @MonthStart), 0)
      + ISNULL((SELECT SUM(rc.CreditsConsumed - rc.PostedCredits)
                FROM dbo.WorkflowDefinitions d
                JOIN dbo.Runs r ON r.WorkflowDefinitionId = d.Id
                JOIN dbo.RunCounters rc ON rc.RunId = r.Id
                WHERE d.WorkspaceId = @WorkspaceId
                  AND r.Status IN (N'Running', N'Failing', N'Cancelling')), 0) AS Credits
);
GO

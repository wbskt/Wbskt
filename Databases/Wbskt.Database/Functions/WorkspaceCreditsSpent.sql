-- The workflow credits @WorkspaceId has spent since @MonthStart: what the ledger holds plus what its
-- runs consumed and have not posted yet (RunCredits_Post), ended or not, so a run that ends between two
-- posts does not hand its spending back to the cap. What a workspace's credit cap is
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
                FROM dbo.RunCounters rc
                JOIN dbo.Runs r ON r.Id = rc.RunId
                JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
                WHERE d.WorkspaceId = @WorkspaceId
                  AND rc.HasUnposted = 1), 0) AS Credits
);
GO

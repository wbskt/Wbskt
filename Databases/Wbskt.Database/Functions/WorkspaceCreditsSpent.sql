-- The workflow credits @WorkspaceId has spent since @MonthStart: what the ledger holds plus what its
-- runs consumed and have not posted yet (dbo.RunCreditsHeld, Unposted). What a workspace's credit cap is
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
      + ISNULL((SELECT SUM(h.Credits)
                FROM dbo.RunCreditsHeld(@WorkspaceId, NULL) h
                WHERE h.Kind = N'Unposted'), 0) AS Credits
);
GO

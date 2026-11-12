-- Deletes the account copies of deleted workspaces that nothing reads any more. dbo.Workspace_Retire
-- keeps a deleted workspace's copy (RetiredAt) so its month still counts on the tenant's credit and
-- message meters and the retention sweeps still reach its history; once neither needs it, it only makes
-- the table, and each tenant's dbo.TenantWorkspaces, longer. A copy goes when all of these hold:
--   - it was retired before the previous month began, and its workspace used no credits or messages
--     since then, so no month a meter or the Business report reads includes it;
--   - no run of it is left (dbo.Run_DeleteRetired reads the copy for the plan's days);
--   - none of its clients has readings left (dbo.ClientReadings_Compact clears a retired workspace's).
-- Returns how many copies went.
CREATE PROCEDURE dbo.WorkspaceAccount_DeleteRetired
    @Now DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PreviousMonthStart DATE = DATEADD(MONTH, -1, DATEFROMPARTS(YEAR(@Now), MONTH(@Now), 1));

    DELETE a
    FROM dbo.WorkspaceAccounts a
    WHERE a.RetiredAt < @PreviousMonthStart
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkspaceCreditUsage u WHERE u.WorkspaceId = a.WorkspaceId AND u.Day >= @PreviousMonthStart)
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMessageUsage m WHERE m.WorkspaceId = a.WorkspaceId AND m.Day >= @PreviousMonthStart)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.WorkflowDefinitions d
                      JOIN dbo.Runs r ON r.WorkflowDefinitionId = d.Id
                      WHERE d.WorkspaceId = a.WorkspaceId)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.Clients c
                      WHERE c.WorkspaceId = a.WorkspaceId
                        AND (EXISTS (SELECT 1 FROM dbo.ClientReadings r WHERE r.ClientId = c.Id)
                             OR EXISTS (SELECT 1 FROM dbo.ClientReadingsHourly h WHERE h.ClientId = c.Id)));

    SELECT @@ROWCOUNT;
END
GO

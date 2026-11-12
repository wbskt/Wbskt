-- Deletes up to @BatchSize account copies of deleted workspaces that nothing reads any more; the caller
-- repeats it until it deletes fewer, so the first run over every copy retired since #739 never holds
-- locks on the whole table. dbo.Workspace_Retire keeps a deleted workspace's copy (RetiredAt) so its
-- month still counts on the tenant's credit and message meters and the retention sweeps still reach
-- its history; once neither needs it, it only makes the table, and each tenant's dbo.TenantWorkspaces,
-- longer. A copy goes when all of these hold:
--   - its workspace used no credits or messages this month or last, the months the meters read
--     (the RetiredAt bound says the same for a workspace that never used any);
--   - no month it sent messages in is still summed live through the copies for the Business invoice
--     (dbo.BusinessUsageOf): one not closed (dbo.BusinessMonths) in which its tenant was tracked on
--     Business, or any month not closed while its tenant is on Business now, which is how a month
--     the tracker never saw is worked out. Other tenants' months are never read for the invoice;
--   - no run of it is left (dbo.Run_DeleteRetired reads the copy for the plan's days);
--   - none of its clients has readings left (dbo.ClientReadings_Compact clears a retired workspace's).
-- dbo.ClientReadings_InsertBatch no longer needs the copy once it is gone: a deleted workspace's clients
-- are all revoked or rejected, and it drops their reports by status. Returns how many copies went.
CREATE PROCEDURE dbo.WorkspaceAccount_DeleteRetired
    @Now       DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PreviousMonthStart DATE = DATEADD(MONTH, -1, DATEFROMPARTS(YEAR(@Now), MONTH(@Now), 1));

    DELETE TOP (@BatchSize) a
    FROM dbo.WorkspaceAccounts a
    WHERE a.RetiredAt < @PreviousMonthStart
      AND NOT EXISTS (SELECT 1
                      FROM dbo.WorkspaceCreditUsage u
                      WHERE u.WorkspaceId = a.WorkspaceId
                        AND u.Day >= @PreviousMonthStart)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.WorkspaceMessageUsage m
                      WHERE m.WorkspaceId = a.WorkspaceId
                        AND m.Day >= @PreviousMonthStart)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.WorkspaceMessageUsage m
                      WHERE m.WorkspaceId = a.WorkspaceId
                        AND NOT EXISTS (SELECT 1
                                        FROM dbo.BusinessMonths b
                                        WHERE b.MonthStart = DATEFROMPARTS(YEAR(m.Day), MONTH(m.Day), 1)
                                          AND b.ClosedAt IS NOT NULL)
                        AND (EXISTS (SELECT 1
                                     FROM dbo.BusinessTenantMonths t
                                     WHERE t.MonthStart = DATEFROMPARTS(YEAR(m.Day), MONTH(m.Day), 1)
                                       AND t.TenantId = a.TenantId)
                             OR EXISTS (SELECT 1 FROM dbo.BusinessTenantsNow() n WHERE n.TenantId = a.TenantId)))
      AND NOT EXISTS (SELECT 1
                      FROM dbo.WorkflowDefinitions d
                      JOIN dbo.Runs r ON r.WorkflowDefinitionId = d.Id
                      WHERE d.WorkspaceId = a.WorkspaceId)
      AND NOT EXISTS (SELECT 1
                      FROM dbo.Clients c
                      WHERE c.WorkspaceId = a.WorkspaceId
                        AND (EXISTS (SELECT 1 FROM dbo.ClientReadings cr WHERE cr.ClientId = c.Id)
                             OR EXISTS (SELECT 1 FROM dbo.ClientReadingsHourly h WHERE h.ClientId = c.Id)));

    SELECT @@ROWCOUNT;
END
GO

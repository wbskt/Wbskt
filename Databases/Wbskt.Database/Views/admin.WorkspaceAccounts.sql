-- The plan and retention each workspace runs under, as the main database last heard it from the auth
-- host. WorkspaceId and TenantId are the Ids in the auth database's admin.Workspaces and admin.Accounts.
CREATE VIEW [admin].WorkspaceAccounts
AS
SELECT
    WA.WorkspaceId,
    WA.TenantId,
    WA.TenantRefId,
    WA.PlanCode,
    WA.PlanRevision,
    WA.LimitOverrides,
    WA.CreditsPerMonth,
    WA.RawReadingsDays,
    WA.HourlyReadingsDays,
    WA.RunHistoryDays,
    WA.RetentionHeldUntil,
    WA.RetiredAt,
    WA.UpdatedAt,
    WA.CreatedAt,
    WCC.MonthlyCreditCap
FROM dbo.WorkspaceAccounts WA
LEFT JOIN dbo.WorkspaceCreditCaps WCC ON WCC.WorkspaceId = WA.WorkspaceId;
GO

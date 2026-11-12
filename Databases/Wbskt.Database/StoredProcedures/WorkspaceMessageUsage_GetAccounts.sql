-- Every workspace whose account (its tenant, or the workspace alone while its plan copy has not arrived)
-- carried client messages since @MonthStart, with its plan copy and the account's pooled total: what the
-- management host compares with each plan's message allowance (MessageAllowanceMonitor). A workspace of
-- such a tenant is listed even if it carried none itself, since the allowance is the tenant's.
CREATE PROCEDURE dbo.WorkspaceMessageUsage_GetAccounts
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    WITH Used AS (
        SELECT WorkspaceId, SUM(Inbound + Outbound) AS Messages
        FROM dbo.WorkspaceMessageUsage
        WHERE Day >= @MonthStart
        GROUP BY WorkspaceId
    ),
    Workspaces AS (
        SELECT a.WorkspaceId, a.TenantId, a.PlanCode, a.LimitOverrides, a.RetiredAt
        FROM dbo.WorkspaceAccounts a
        UNION ALL
        SELECT u.WorkspaceId, NULL, NULL, NULL, NULL
        FROM Used u
        WHERE NOT EXISTS (SELECT 1 FROM dbo.WorkspaceAccounts a WHERE a.WorkspaceId = u.WorkspaceId)
    ),
    Pooled AS (
        SELECT
            w.WorkspaceId,
            w.TenantId,
            w.PlanCode,
            w.LimitOverrides,
            w.RetiredAt,
            CASE
                WHEN w.TenantId IS NULL THEN ISNULL(u.Messages, 0)
                ELSE SUM(ISNULL(u.Messages, 0)) OVER (PARTITION BY w.TenantId)
            END AS AccountMessages
        FROM Workspaces w
        LEFT JOIN Used u ON u.WorkspaceId = w.WorkspaceId
    )
    SELECT
        WorkspaceId,
        TenantId,
        PlanCode,
        LimitOverrides,
        AccountMessages
    FROM Pooled
    WHERE AccountMessages > 0
      -- A deleted workspace's messages still count in its tenant's pool, but it is not listed: nothing
      -- is left of it to warn or throttle.
      AND RetiredAt IS NULL;
END
GO

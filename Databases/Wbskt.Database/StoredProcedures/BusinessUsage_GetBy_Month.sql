-- What each tenant whose plan is Business used in one calendar month (UTC), for the overage invoice:
-- client messages from the message meter (dbo.WorkspaceMessageUsage) across its workspaces, and the
-- workflow credits its workspaces posted that month with the part drawn from its credit packs
-- (dbo.TenantCreditMonths). The plan is the tenant's latest copy here, so the report reads a contract
-- as it stands, not as it stood during the month.
CREATE PROCEDURE dbo.BusinessUsage_GetBy_Month
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextMonth DATE = DATEADD(MONTH, 1, @MonthStart);

    WITH Latest AS
    (
        SELECT
            TenantId,
            TenantRefId,
            PlanCode,
            LimitOverrides,
            ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY PlanRevision DESC, WorkspaceId) AS Pick
        FROM dbo.WorkspaceAccounts
        -- A deleted workspace's copy stops following the tenant's plan, so it is not read for the plan;
        -- its messages still count below.
        WHERE RetiredAt IS NULL
    )
    SELECT
        t.TenantRefId,
        t.LimitOverrides,
        ISNULL((SELECT SUM(u.Inbound + u.Outbound)
                FROM dbo.WorkspaceMessageUsage u
                JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = u.WorkspaceId
                WHERE a.TenantId = t.TenantId
                  AND u.Day >= @MonthStart
                  AND u.Day < @NextMonth), 0) AS MessagesUsed,
        ISNULL(m.Posted, 0) AS CreditsPosted,
        ISNULL(m.Drawn, 0) AS CreditsDrawn
    FROM Latest t
    LEFT JOIN dbo.TenantCreditMonths m ON m.TenantId = t.TenantId AND m.MonthStart = @MonthStart
    WHERE t.Pick = 1
      AND t.PlanCode = N'Business'
    ORDER BY t.TenantRefId;
END

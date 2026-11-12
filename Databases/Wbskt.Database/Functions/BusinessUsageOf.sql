-- What each tenant on Business in the calendar month (UTC) from @MonthStart used, worked out now: the
-- tenants dbo.BusinessTenantMonth_Track saw on Business that month, with the contract as last seen, and
-- any tenant on Business now whose plan copy has not changed since before the month ended (one the
-- tracker missed, or a month before it ran). Client messages are summed from the message meter
-- (dbo.WorkspaceMessageUsage) across every workspace the tenant has had, deleted ones included (their
-- copies are kept, see dbo.WorkspaceAccounts), and credits are what the month posted with the part drawn
-- from credit packs (dbo.TenantCreditMonths).
CREATE FUNCTION dbo.BusinessUsageOf (@MonthStart DATE)
RETURNS TABLE
AS
RETURN
(
    WITH Tenants AS
    (
        SELECT
            b.TenantId,
            b.TenantRefId,
            b.LimitOverrides
        FROM dbo.BusinessTenantMonths b
        WHERE b.MonthStart = @MonthStart

        UNION ALL

        SELECT
            n.TenantId,
            n.TenantRefId,
            n.LimitOverrides
        FROM dbo.BusinessTenantsNow() n
        WHERE n.UpdatedAt < DATEADD(MONTH, 1, @MonthStart)
          AND NOT EXISTS (SELECT 1
                          FROM dbo.BusinessTenantMonths b
                          WHERE b.MonthStart = @MonthStart
                            AND b.TenantId = n.TenantId)
    )
    SELECT
        t.TenantId,
        t.TenantRefId,
        t.LimitOverrides,
        ISNULL((SELECT SUM(u.Inbound + u.Outbound)
                FROM dbo.WorkspaceMessageUsage u
                JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = u.WorkspaceId
                WHERE a.TenantId = t.TenantId
                  AND u.Day >= @MonthStart
                  AND u.Day < DATEADD(MONTH, 1, @MonthStart)), 0) AS MessagesUsed,
        ISNULL(m.Posted, 0) AS CreditsPosted,
        ISNULL(m.Drawn, 0) AS CreditsDrawn
    FROM Tenants t
    LEFT JOIN dbo.TenantCreditMonths m ON m.TenantId = t.TenantId AND m.MonthStart = @MonthStart
);
GO

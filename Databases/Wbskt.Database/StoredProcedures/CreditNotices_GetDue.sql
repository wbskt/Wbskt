-- The credit notices owed for the month starting @MonthStart: each workspace whose posted spend has
-- reached 80% or 100% of its cap, and each tenant whose posted credits have reached 80% or 100% of its
-- plan's allowance, past what it was already told (NotifiedPercent). Level is the highest share reached
-- and Notified what was announced; the engine (CreditUsagePoster) publishes each threshold between them
-- and marks it after the publish, so a notice lost to a refused publish, an engine stopping between the
-- post and the publish, or credits posted by Run_DeleteRetired, is found again here.
-- Spend is what the ledger holds, as the posts saw it: the unposted credits are announced once posted.
CREATE PROCEDURE dbo.CreditNotices_GetDue
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MonthEnd DATE = DATEADD(MONTH, 1, @MonthStart);

    SELECT
        N'Cap' AS Kind,
        c.WorkspaceId,
        CAST(NULL AS INT) AS TenantId,
        c.MonthlyCreditCap AS Limit,
        s.Spent,
        l.Level,
        n.Notified
    FROM dbo.WorkspaceCreditCaps c
    CROSS APPLY (SELECT ISNULL(SUM(u.Credits), 0) AS Spent
                 FROM dbo.WorkspaceCreditUsage u
                 WHERE u.WorkspaceId = c.WorkspaceId
                   AND u.Day >= @MonthStart
                   AND u.Day < @MonthEnd) s
    CROSS APPLY (SELECT CASE WHEN s.Spent >= c.MonthlyCreditCap THEN 100
                             WHEN s.Spent >= c.MonthlyCreditCap * 0.8 THEN 80
                             ELSE 0 END AS Level) l
    CROSS APPLY (SELECT CASE WHEN c.NotifiedMonthStart = @MonthStart THEN c.NotifiedPercent ELSE 0 END AS Notified) n
    WHERE c.MonthlyCreditCap > 0
      AND l.Level > n.Notified

    UNION ALL

    -- The allowance is the tenant's plan's from a live copy of its account, or from a deleted
    -- workspace's copy (which keeps the one it last had) when none is live, as RunCredits_Post and
    -- Run_DeleteRetired take it; the notice names the live workspace that posted the most this
    -- month, or a deleted one only when none is left. Read through IX_TenantCreditMonths_MonthStart.
    SELECT
        N'Allowance',
        w.WorkspaceId,
        m.TenantId,
        a.Allowance,
        m.Posted,
        l.Level,
        m.NotifiedPercent
    FROM dbo.TenantCreditMonths m
    CROSS APPLY (SELECT COALESCE(MAX(CASE WHEN wa.RetiredAt IS NULL THEN wa.CreditsPerMonth END),
                                 MAX(wa.CreditsPerMonth)) AS Allowance
                 FROM dbo.WorkspaceAccounts wa
                 WHERE wa.TenantId = m.TenantId) a
    CROSS APPLY (SELECT CASE WHEN m.Posted >= a.Allowance THEN 100
                             WHEN m.Posted >= a.Allowance * 0.8 THEN 80
                             ELSE 0 END AS Level) l
    CROSS APPLY (SELECT TOP (1) wa.WorkspaceId
                 FROM dbo.WorkspaceAccounts wa
                 LEFT JOIN dbo.WorkspaceCreditUsage u
                     ON u.WorkspaceId = wa.WorkspaceId
                    AND u.Day >= @MonthStart
                    AND u.Day < @MonthEnd
                 WHERE wa.TenantId = m.TenantId
                 GROUP BY wa.WorkspaceId
                 ORDER BY MAX(CASE WHEN wa.RetiredAt IS NULL THEN 0 ELSE 1 END),
                          ISNULL(SUM(u.Credits), 0) DESC,
                          wa.WorkspaceId) w
    WHERE m.MonthStart = @MonthStart
      AND a.Allowance > 0
      AND l.Level > m.NotifiedPercent;
END
GO

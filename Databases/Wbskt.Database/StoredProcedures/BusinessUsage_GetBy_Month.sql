-- What each tenant on Business in one calendar month (UTC) used, for the overage invoice: client
-- messages, and the workflow credits its workspaces posted with the part drawn from its credit packs,
-- with the contract it was on. A closed month (dbo.BusinessMonth_Close) is read as it was closed; a month
-- not closed yet is worked out now (dbo.BusinessUsageOf).
CREATE PROCEDURE dbo.BusinessUsage_GetBy_Month
    @MonthStart DATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.BusinessMonths WHERE MonthStart = @MonthStart AND ClosedAt IS NOT NULL)
    BEGIN
        SELECT
            TenantRefId,
            LimitOverrides,
            MessagesUsed,
            CreditsPosted,
            CreditsDrawn
        FROM dbo.BusinessTenantMonths
        WHERE MonthStart = @MonthStart
        ORDER BY TenantRefId;
    END
    ELSE
    BEGIN
        SELECT
            TenantRefId,
            LimitOverrides,
            MessagesUsed,
            CreditsPosted,
            CreditsDrawn
        FROM dbo.BusinessUsageOf(@MonthStart)
        ORDER BY TenantRefId;
    END
END
GO

-- Where a plan refuses new runs for the month's workflow credits, from a plan copy
-- (dbo.WorkspaceAccounts.PlanCode and CreditsPerMonth): the allowance itself, except on Business, whose
-- runs carry on to twice it (BusinessContract.CreditOverageMultiple, PlanCredits.RunsRefusedAt; pinned
-- by an integration test). NULL for a copy without CreditsPerMonth. RunCounters_TryCharge reads it so a
-- run going through a plan change extends its lease against the plan the tenant is on now.
CREATE FUNCTION dbo.PlanCreditsRefusedAt (@PlanCode NVARCHAR(20), @CreditsPerMonth DECIMAL(18,4))
RETURNS DECIMAL(18,4)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CASE WHEN @PlanCode = N'Business' THEN @CreditsPerMonth * 2 ELSE @CreditsPerMonth END;
END
GO

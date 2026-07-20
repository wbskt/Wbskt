CREATE PROCEDURE dbo.RunCounters_TryCharge
    @RunId INT,
    @Cost  DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;

    -- Atomic budget-checked charge: the WHERE clause re-checks the budget under the same
    -- row lock as the increment, so concurrent callers on the same run can never overshoot
    -- CreditBudget between them. Zero rows returned means the run is out of credits.
    UPDATE rc WITH (ROWLOCK)
    SET rc.CreditsConsumed = rc.CreditsConsumed + @Cost,
        rc.UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.CreditsConsumed
    FROM dbo.RunCounters rc
    JOIN dbo.Runs r ON r.Id = rc.RunId
    WHERE rc.RunId = @RunId
      AND rc.CreditsConsumed + @Cost <= r.CreditBudget;
END;
GO

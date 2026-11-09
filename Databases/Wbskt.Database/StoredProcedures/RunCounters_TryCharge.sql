CREATE PROCEDURE dbo.RunCounters_TryCharge
    @RunId INT,
    @Cost  DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Atomic budget-checked charge: the WHERE clause re-checks the budget under the same row lock as the
    -- increment, so concurrent callers on the same run can never overshoot CreditBudget between them.
    -- No row returned means the run is out of credits; otherwise the row is what was spent of the budget
    -- the charge was held to, and that budget.
    DECLARE @Charged TABLE (Spent DECIMAL(18,4) NOT NULL, Budget DECIMAL(18,4) NOT NULL);

    -- A run with its own budget: one statement, as on every charge before sub-workflow budgets.
    UPDATE rc WITH (ROWLOCK)
    SET rc.CreditsConsumed = rc.CreditsConsumed + @Cost,
        rc.UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.CreditsConsumed + inserted.ChildCreditsConsumed, r.CreditBudget INTO @Charged (Spent, Budget)
    FROM dbo.RunCounters rc
    JOIN dbo.Runs r ON r.Id = rc.RunId
    WHERE rc.RunId = @RunId
      AND rc.BudgetRunId IS NULL
      AND rc.CreditsConsumed + rc.ChildCreditsConsumed + @Cost <= r.CreditBudget;

    IF @@ROWCOUNT = 0
    BEGIN
        -- A sub-workflow child: held to its root run's budget, which the root and all its children share.
        -- The root's row first, then the child's, the order Run_Start takes them in.
        DECLARE @BudgetRunId INT = (SELECT BudgetRunId FROM dbo.RunCounters WHERE RunId = @RunId);
        IF @BudgetRunId IS NOT NULL
        BEGIN
            BEGIN TRANSACTION;

            UPDATE rc WITH (ROWLOCK)
            SET rc.ChildCreditsConsumed = rc.ChildCreditsConsumed + @Cost,
                rc.UpdatedAt = SYSUTCDATETIME()
            OUTPUT inserted.CreditsConsumed + inserted.ChildCreditsConsumed, r.CreditBudget INTO @Charged (Spent, Budget)
            FROM dbo.RunCounters rc
            JOIN dbo.Runs r ON r.Id = rc.RunId
            WHERE rc.RunId = @BudgetRunId
              AND rc.CreditsConsumed + rc.ChildCreditsConsumed + @Cost <= r.CreditBudget;

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.RunCounters WITH (ROWLOCK)
                SET CreditsConsumed = CreditsConsumed + @Cost,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE RunId = @RunId;
            END

            COMMIT TRANSACTION;
        END
    END

    SELECT Spent, Budget FROM @Charged;
END;
GO

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
    -- the charge was held to, and that budget (its ceiling, for a leased one).
    DECLARE @Charged TABLE (Spent DECIMAL(18,4) NOT NULL, Budget DECIMAL(18,4) NOT NULL);

    -- A run with its own budget and room in it: one statement, as on every charge before sub-workflow
    -- budgets and leases.
    UPDATE rc WITH (ROWLOCK)
    SET rc.CreditsConsumed = rc.CreditsConsumed + @Cost,
        rc.HasUnposted = CASE WHEN @Cost <> 0 THEN 1 ELSE rc.HasUnposted END,
        rc.UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.CreditsConsumed + inserted.ChildCreditsConsumed, COALESCE(inserted.BudgetCeiling, r.CreditBudget) INTO @Charged (Spent, Budget)
    FROM dbo.RunCounters rc
    JOIN dbo.Runs r ON r.Id = rc.RunId
    WHERE rc.RunId = @RunId
      AND rc.BudgetRunId IS NULL
      AND rc.CreditsConsumed + rc.ChildCreditsConsumed + @Cost <= r.CreditBudget;

    IF @@ROWCOUNT = 0
    BEGIN
        -- A sub-workflow child is held to its root run's budget, which the root and all its children
        -- share; a run with its own budget is its own root.
        DECLARE @BudgetRunId INT = (SELECT BudgetRunId FROM dbo.RunCounters WHERE RunId = @RunId);
        DECLARE @Root INT = COALESCE(@BudgetRunId, @RunId);

        BEGIN TRANSACTION;

        -- A leased budget that the charge would pass is extended from what the tenant's monthly credits
        -- and the workspace's cap have left, under their locks (as Run_Start takes them, the tenant's
        -- first, and both before any counter row): by the larger of what the charge needs and a lease (the
        -- run's lease size, no more than a twentieth of what the tenant has left or of the cap), never
        -- past the ceiling or what either has left. The tenant and its allowance are read from the plan
        -- copy as it is now (dbo.PlanCreditsRefusedAt), so a run going through a plan change is held to
        -- the new plan, and one started before its workspace's copy arrived counts with its tenant once
        -- it has; the allowance it started under stands in for a copy without one. With neither held to
        -- (the cap removed since, on a run started without an allowance), the budget goes straight to its
        -- ceiling. The extension is reserved from then on, as the lease it adds to was.
        DECLARE @Ceiling DECIMAL(18,4);
        DECLARE @WorkspaceId INT;
        DECLARE @Allowance DECIMAL(18,4);
        DECLARE @RunLease DECIMAL(18,4);
        DECLARE @TenantId INT;
        SELECT @Ceiling = rc.BudgetCeiling,
               @WorkspaceId = d.WorkspaceId,
               @Allowance = CASE WHEN rc.CreditAllowance IS NULL THEN NULL
                                 ELSE COALESCE(dbo.PlanCreditsRefusedAt(wa.PlanCode, wa.CreditsPerMonth), rc.CreditAllowance) END,
               @RunLease = rc.CreditLease,
               @TenantId = wa.TenantId
        FROM dbo.RunCounters rc
        JOIN dbo.Runs r ON r.Id = rc.RunId
        JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
        LEFT JOIN dbo.WorkspaceAccounts wa ON wa.WorkspaceId = d.WorkspaceId
        WHERE rc.RunId = @Root;

        IF @Ceiling IS NOT NULL
           AND EXISTS (SELECT 1
                       FROM dbo.RunCounters rc
                       JOIN dbo.Runs r ON r.Id = rc.RunId
                       WHERE rc.RunId = @Root
                         AND r.CreditBudget < rc.BudgetCeiling
                         AND rc.CreditsConsumed + rc.ChildCreditsConsumed + @Cost > r.CreditBudget)
        BEGIN
            IF @Allowance IS NOT NULL
            BEGIN
                DECLARE @CreditsResource NVARCHAR(255) = dbo.PlanLimitLockResource(N'credits', @WorkspaceId, @TenantId);
                DECLARE @CreditsLockResult INT;
                EXEC @CreditsLockResult = sp_getapplock @Resource = @CreditsResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
                IF @CreditsLockResult < 0
                    THROW 50035, 'The account is too busy to extend the run''s credit budget; try again.', 1;
            END

            DECLARE @CapResource NVARCHAR(255) = CONCAT(N'credit-cap:', @WorkspaceId);
            DECLARE @CapLockResult INT;
            EXEC @CapLockResult = sp_getapplock @Resource = @CapResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @CapLockResult < 0
                THROW 50035, 'The workspace is too busy to extend the run''s credit budget; try again.', 1;

            -- What the tenant and the cap have free is read before the run's own rows are locked:
            -- RunCredits_Post locks counter rows in its own order, and reading the others' counters while
            -- holding the root's could meet it the other way round. The root's spent and reserved parts
            -- add up to the same whether a charge lands on it meanwhile or not, so the figures hold.
            DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
            DECLARE @TenantLeft DECIMAL(18,4);
            IF @Allowance IS NOT NULL
                SET @TenantLeft = (SELECT Credits FROM dbo.TenantCreditsLeft(@WorkspaceId, @TenantId, @Allowance, @Now));

            DECLARE @Cap DECIMAL(18,4) = (SELECT MonthlyCreditCap FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId);
            DECLARE @CapLeft DECIMAL(18,4);
            IF @Cap IS NOT NULL
            BEGIN
                DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Now), MONTH(@Now), 1);
                SET @CapLeft = @Cap
                    - (SELECT Credits FROM dbo.WorkspaceCreditsSpent(@WorkspaceId, @MonthStart))
                    - (SELECT Credits FROM dbo.WorkspaceCreditsReserved(@WorkspaceId));
            END

            DECLARE @Budget DECIMAL(18,4);
            DECLARE @Spent DECIMAL(18,4);
            SELECT @Budget = r.CreditBudget, @Spent = rc.CreditsConsumed + rc.ChildCreditsConsumed
            FROM dbo.RunCounters rc WITH (UPDLOCK)
            JOIN dbo.Runs r WITH (UPDLOCK) ON r.Id = rc.RunId
            WHERE rc.RunId = @Root;

            -- Spent can be past the budget: a charge that landed as WorkspaceCreditCap_Set cut it, or a
            -- child's start cost, which Run_Start charges on the root whether it has room or not. That
            -- excess is already in @CapLeft and @TenantLeft as spent, so it is not asked of them again.
            IF @Spent > @Budget
                SET @Budget = @Spent;

            DECLARE @Need DECIMAL(18,4) = @Spent + @Cost - @Budget;
            IF @Need > 0 AND @Budget < @Ceiling
            BEGIN
                DECLARE @Extend DECIMAL(18,4) = @Ceiling - @Budget;
                DECLARE @Lease DECIMAL(18,4);
                IF @Allowance IS NOT NULL
                BEGIN
                    SET @Lease = COALESCE(@RunLease, @Allowance / 20);
                    IF @TenantLeft / 20 < @Lease
                        SET @Lease = @TenantLeft / 20;
                END
                IF @Cap IS NOT NULL AND (@Lease IS NULL OR @Cap / 20 < @Lease)
                    SET @Lease = @Cap / 20;

                IF @Lease IS NOT NULL
                BEGIN
                    IF @Lease < @Need
                        SET @Lease = @Need;
                    IF @Lease < @Extend
                        SET @Extend = @Lease;
                END
                IF @CapLeft < @Extend
                    SET @Extend = @CapLeft;
                IF @TenantLeft < @Extend
                    SET @Extend = @TenantLeft;

                -- Only an extension that lets the charge through is taken, so a refused charge leaves
                -- nothing held by a run that cannot use it.
                IF @Extend >= @Need
                    UPDATE dbo.Runs
                    SET CreditBudget = @Budget + @Extend
                    WHERE Id = @Root;
            END
        END

        UPDATE rc WITH (ROWLOCK)
        SET rc.CreditsConsumed = rc.CreditsConsumed + CASE WHEN @BudgetRunId IS NULL THEN @Cost ELSE 0 END,
            rc.HasUnposted = CASE WHEN @BudgetRunId IS NULL AND @Cost <> 0 THEN 1 ELSE rc.HasUnposted END,
            rc.ChildCreditsConsumed = rc.ChildCreditsConsumed + CASE WHEN @BudgetRunId IS NULL THEN 0 ELSE @Cost END,
            rc.UpdatedAt = SYSUTCDATETIME()
        OUTPUT inserted.CreditsConsumed + inserted.ChildCreditsConsumed, COALESCE(inserted.BudgetCeiling, r.CreditBudget) INTO @Charged (Spent, Budget)
        FROM dbo.RunCounters rc
        JOIN dbo.Runs r ON r.Id = rc.RunId
        WHERE rc.RunId = @Root
          AND rc.CreditsConsumed + rc.ChildCreditsConsumed + @Cost <= r.CreditBudget;

        IF @@ROWCOUNT > 0 AND @BudgetRunId IS NOT NULL
        BEGIN
            UPDATE dbo.RunCounters WITH (ROWLOCK)
            SET CreditsConsumed = CreditsConsumed + @Cost,
                HasUnposted = CASE WHEN @Cost <> 0 THEN 1 ELSE HasUnposted END,
                UpdatedAt = SYSUTCDATETIME()
            WHERE RunId = @RunId;
        END

        COMMIT TRANSACTION;
    END

    SELECT Spent, Budget FROM @Charged;
END;
GO

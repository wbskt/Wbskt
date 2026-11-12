CREATE PROCEDURE dbo.Run_Start
    @RefId                UNIQUEIDENTIFIER,
    @WorkflowDefinitionId INT,
    @WorkflowRefId        UNIQUEIDENTIFIER,
    @WorkflowVersion      INT,
    @TriggerNodeId        UNIQUEIDENTIFIER,
    @CorrelationKey       NVARCHAR(400),
    @StartedAt            DATETIME2(3),
    @CreditBudget         DECIMAL(18,4),
    @TriggerStartKey      NVARCHAR(200),
    @BranchRefId          UNIQUEIDENTIFIER,
    @BranchLocalJson      NVARCHAR(MAX),
    @WorkspaceId          INT = NULL,
    -- The plan's limit on runs going at once, pooled across @TenantId's workspaces (50026).
    @MaxActiveRuns        INT = NULL,
    @TenantId             INT = NULL,
    @ParentRunRefId       UNIQUEIDENTIFIER = NULL,
    @ActiveRunGuard       TINYINT = 0,
    -- A test run (see dbo.WorkflowDefinitions.Kind): at most one goes at a time per workflow (50029).
    @IsTest               BIT = 0,
    -- Credits charged for creating the run; seeded into its counters so the charge commits with it.
    @StartCost            DECIMAL(18,4) = 0,
    -- The plan's workflow credits a calendar month (UTC), pooled across @TenantId's workspaces (50033),
    -- and with it the workspace's own cap (50034). On Business this is a multiple of the allowance
    -- (PlanCredits.RunsRefusedAt), since its runs carry on past it and the overage is invoiced. NULL
    -- for a sub-workflow child, which spends from its parent's budget instead.
    @CreditAllowance      DECIMAL(18,4) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- A triggered run is created whole: the run, its counters (seeded with the one branch it starts
    -- with) and its root branch commit together, so a run is never found without the branch that
    -- drives it. With a @TriggerStartKey, an attempt that finds the key already taken returns the run
    -- that holds it instead of starting a second one: that is how a retried inbound event recovers the
    -- run an earlier attempt created before it failed.
    DECLARE @RunId INT;
    DECLARE @Created BIT = 0;

    IF @TriggerStartKey IS NOT NULL
    BEGIN
        SELECT @RunId = Id FROM dbo.Runs WHERE TriggerStartKey = @TriggerStartKey;
    END

    IF @RunId IS NULL
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            -- The concurrency policy read the active runs before this call; another event with the
            -- same key may have started one since (#467). Checked again under a lock per workflow,
            -- trigger and key so concurrent starts take turns. ActiveRunGuard: 1 refuses while any run
            -- is active (DropIfRunning, Queue), 2 ignores Cancelling, which is where CancelExisting has
            -- just put the runs it found. Taken before the plan lock below, always in that order.
            IF @ActiveRunGuard > 0 AND @CorrelationKey IS NOT NULL
            BEGIN
                DECLARE @KeyResource NVARCHAR(255) = dbo.TriggerStartLockResource(@WorkflowRefId, @TriggerNodeId, @CorrelationKey);
                DECLARE @KeyLockResult INT;
                EXEC @KeyLockResult = sp_getapplock @Resource = @KeyResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
                IF @KeyLockResult < 0
                    THROW 50028, 'Another start for this trigger and correlation key is in progress.', 1;

                -- The Status IN list is IX_Runs_WorkflowRefId_CorrelationKey_Active's filter word for word;
                -- any other shape of it and the optimizer scans dbo.Runs instead.
                IF EXISTS (SELECT 1
                           FROM dbo.Runs
                           WHERE WorkflowRefId = @WorkflowRefId
                             AND TriggerNodeId = @TriggerNodeId
                             AND CorrelationKey = @CorrelationKey
                             AND Status IN (N'Running', N'Failing', N'Cancelling')
                             AND (@ActiveRunGuard = 1 OR Status <> N'Cancelling'))
                    THROW 50028, 'A run is already active for this trigger and correlation key.', 1;
            END

            -- A run started by a test run's sub-workflow step is a test run too, so a test never shows in
            -- the child workflow's stats. Only a test started by hand is held to one at a time below.
            DECLARE @RunIsTest BIT = @IsTest;
            IF @IsTest = 0 AND @ParentRunRefId IS NOT NULL
                SELECT @RunIsTest = IsTest FROM dbo.Runs WHERE RefId = @ParentRunRefId;

            -- One test run at a time per workflow, so a test that waits or loops cannot pile up behind the
            -- author's next click. Counted under a lock per workflow, taken in the same order as the others.
            IF @IsTest = 1
            BEGIN
                DECLARE @TestResource NVARCHAR(255) = CONCAT(N'test-run:', @WorkflowRefId);
                DECLARE @TestLockResult INT;
                EXEC @TestLockResult = sp_getapplock @Resource = @TestResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
                IF @TestLockResult < 0
                    THROW 50029, 'Another test run of this workflow is starting.', 1;

                -- IX_Runs_WorkflowRefId_Test_Active's filter word for word.
                IF EXISTS (SELECT 1
                           FROM dbo.Runs
                           WHERE WorkflowRefId = @WorkflowRefId
                             AND IsTest = 1
                             AND Status IN (N'Running', N'Failing', N'Cancelling'))
                    THROW 50029, 'A test run of this workflow is already going.', 1;

                -- A deleted workflow's runs were cancelled with it; a test copy made just before must not
                -- start one after. WorkflowDefinition_Delete writes the tombstone before it reads the runs.
                IF EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WHERE RefId = @WorkflowRefId)
                    THROW 50022, 'The workflow was deleted.', 1;
            END

            -- The caller's tenant came from its own copy of the workspace's account; this database's
            -- copy is the one the engine's other locks are named by (RunCounters_TryCharge reads it), so
            -- it wins whenever it has arrived.
            IF @WorkspaceId IS NOT NULL
                SET @TenantId = COALESCE((SELECT TenantId FROM dbo.WorkspaceAccounts WHERE WorkspaceId = @WorkspaceId), @TenantId);

            -- The plan's limit on runs going at once (Running, Failing or Cancelling, as in
            -- IX_Runs_WorkflowRefId_CorrelationKey_Active), pooled across the tenant's workspaces
            -- (dbo.TenantWorkspaces). Counted under an application lock per tenant, so concurrent
            -- starts one short of the limit cannot all pass the count. NULL is no limit. The wait for
            -- the lock is bounded: a start that cannot get it in a few seconds is refused as busy
            -- (50035, which the caller retries), rather than holding an inbound request (and a pooled
            -- connection) for as long as the tenant stays contended.
            IF @MaxActiveRuns IS NOT NULL AND @WorkspaceId IS NOT NULL
            BEGIN
                DECLARE @Resource NVARCHAR(255) = dbo.PlanLimitLockResource(N'runs', @WorkspaceId, @TenantId);
                DECLARE @LockResult INT;
                EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
                IF @LockResult < 0
                    THROW 50035, 'The account is too busy to start another run; try again.', 1;

                -- IX_Runs_WorkflowDefinitionId_Active keeps this to the runs still going.
                IF (SELECT COUNT(*)
                    FROM dbo.Runs r
                    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
                    JOIN dbo.TenantWorkspaces(@WorkspaceId, @TenantId) w ON w.WorkspaceId = d.WorkspaceId
                    WHERE r.Status IN (N'Running', N'Failing', N'Cancelling')) >= @MaxActiveRuns
                    THROW 50026, 'The plan has reached its limit of active runs.', 1;
            END

            -- The plan's monthly credits, pooled like the runs above, under an application lock per
            -- tenant (as RunCounters_TryCharge takes it to extend a lease), so two starts cannot both take
            -- the same remainder. What is left (dbo.TenantCreditsLeft) is what the plan has not covered
            -- this calendar month (UTC), plus what credit grants still hold, less what runs have spent and
            -- not posted and what the runs going hold of their budgets. The run's budget is cut to that
            -- and becomes its ceiling, and the run is given a lease of it, which RunCounters_TryCharge
            -- extends as it is spent: one run going does not hold the whole remainder away from the next,
            -- and runs going at once never spend past it between them. A lease is the allowance shared
            -- among as many runs as the plan lets go at once (at least twenty), so runs parked for days
            -- cannot hold it all while the plan would let more start, and no more than a twentieth of what
            -- is left, so late in the month they hold less of it. It is never less than the start cost.
            -- NULL is no limit.
            DECLARE @BudgetCeiling DECIMAL(18,4);
            DECLARE @RunCreditAllowance DECIMAL(18,4);
            DECLARE @RunCreditLease DECIMAL(18,4);
            IF @CreditAllowance IS NOT NULL AND @WorkspaceId IS NOT NULL
            BEGIN
                DECLARE @CreditsResource NVARCHAR(255) = dbo.PlanLimitLockResource(N'credits', @WorkspaceId, @TenantId);
                DECLARE @CreditsLockResult INT;
                EXEC @CreditsLockResult = sp_getapplock @Resource = @CreditsResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
                IF @CreditsLockResult < 0
                    THROW 50035, 'The account is too busy to start another run; try again.', 1;

                DECLARE @CreditsLeft DECIMAL(18,4) =
                    (SELECT Credits FROM dbo.TenantCreditsLeft(@WorkspaceId, @TenantId, @CreditAllowance, @StartedAt));

                IF @CreditsLeft <= 0 OR @CreditsLeft < @StartCost
                    THROW 50033, 'The plan has used its workflow credits for this month.', 1;

                IF @CreditsLeft < @CreditBudget
                    SET @CreditBudget = @CreditsLeft;

                -- A start exempt from the runs limit (@MaxActiveRuns NULL) shares it twenty ways.
                DECLARE @Lease DECIMAL(18,4) = @CreditAllowance / CASE WHEN @MaxActiveRuns > 20 THEN @MaxActiveRuns ELSE 20 END;
                SET @RunCreditLease = @Lease;
                IF @CreditsLeft / 20 < @Lease
                    SET @Lease = @CreditsLeft / 20;

                -- The workspace's own cap, if its owner set one (dbo.WorkspaceCreditCaps), checked after the
                -- tenant's allowance, so the error names the limit that is actually spent when both are. It
                -- is held the same way, under a lock per workspace: what the workspace spent this month plus
                -- what its runs going may yet spend (each root run's unspent budget, which its sub-workflow
                -- children share) is reserved against it, and the lease is no more than a twentieth of the
                -- cap. A run's charges never pass its budget, so the workspace never passes its cap, but for
                -- charges already under way when dbo.WorkspaceCreditCap_Set cuts a going run's budget (see
                -- there).
                -- Read with HOLDLOCK and held to the commit, so a cap being set (WorkspaceCreditCap_Set
                -- writes the row before it cuts the runs going) waits for this start or is seen by it,
                -- and this run is never left out of the cut. The set holds the row while it waits up to
                -- two seconds for the cap's lock and then cuts the runs going.
                DECLARE @Cap DECIMAL(18,4) =
                    (SELECT MonthlyCreditCap FROM dbo.WorkspaceCreditCaps WITH (HOLDLOCK) WHERE WorkspaceId = @WorkspaceId);
                IF @Cap IS NOT NULL
                BEGIN
                    DECLARE @CapResource NVARCHAR(255) = CONCAT(N'credit-cap:', @WorkspaceId);
                    DECLARE @CapLockResult INT;
                    EXEC @CapLockResult = sp_getapplock @Resource = @CapResource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
                    IF @CapLockResult < 0
                        THROW 50035, 'The workspace is too busy to start another run; try again.', 1;

                    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@StartedAt), MONTH(@StartedAt), 1);
                    DECLARE @WorkspaceSpent DECIMAL(18,4) = (SELECT Credits FROM dbo.WorkspaceCreditsSpent(@WorkspaceId, @MonthStart));
                    DECLARE @WorkspaceReserved DECIMAL(18,4) = (SELECT Credits FROM dbo.WorkspaceCreditsReserved(@WorkspaceId));
                    DECLARE @CapLeft DECIMAL(18,4) = @Cap - @WorkspaceSpent - @WorkspaceReserved;

                    IF @CapLeft <= 0 OR @CapLeft < @StartCost
                        THROW 50034, 'The workspace has used its monthly credit cap.', 1;

                    IF @CapLeft < @CreditBudget
                        SET @CreditBudget = @CapLeft;

                    IF @Cap / 20 < @Lease
                        SET @Lease = @Cap / 20;
                END

                SET @BudgetCeiling = @CreditBudget;
                SET @RunCreditAllowance = @CreditAllowance;
                IF @Lease < @StartCost
                    SET @Lease = @StartCost;
                IF @Lease < @CreditBudget
                    SET @CreditBudget = @Lease;
            END

            -- A sub-workflow child spends from its root run's budget (dbo.RunCounters.BudgetRunId): its
            -- parent was checked against the plan when it started, so a parent never fails halfway
            -- because its child was refused. Its own CreditBudget shows the budget it shares. Its start
            -- charge goes on the root like its later charges, taken whether or not the root has room, as
            -- a charge already made cannot be refused. The root's counter row is locked here, before the
            -- child's is written, the order RunCounters_TryCharge takes them in.
            DECLARE @BudgetRunId INT;
            IF @ParentRunRefId IS NOT NULL
            BEGIN
                SELECT @BudgetRunId = COALESCE(rc.BudgetRunId, rc.RunId)
                FROM dbo.Runs p
                JOIN dbo.RunCounters rc ON rc.RunId = p.Id
                WHERE p.RefId = @ParentRunRefId;

                IF @BudgetRunId IS NOT NULL
                BEGIN
                    UPDATE rc WITH (ROWLOCK)
                    SET rc.ChildCreditsConsumed = rc.ChildCreditsConsumed + @StartCost,
                        rc.UpdatedAt = SYSUTCDATETIME()
                    FROM dbo.RunCounters rc
                    WHERE rc.RunId = @BudgetRunId;

                    SELECT @CreditBudget = COALESCE(rc.BudgetCeiling, r.CreditBudget)
                    FROM dbo.Runs r
                    JOIN dbo.RunCounters rc ON rc.RunId = r.Id
                    WHERE r.Id = @BudgetRunId;
                END
            END

            -- Last, after every check that can refuse the start, so the lock it takes on the workflow's
            -- counter row is held only for the inserts below and a refused start uses no number.
            DECLARE @Number INT;
            EXEC dbo.WorkflowRunSequence_Next @WorkflowRefId = @WorkflowRefId, @Number = @Number OUTPUT;

            INSERT INTO dbo.Runs
                (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, CorrelationKey, Status, StartedAt, CreditBudget, TriggerStartKey, ParentRunRefId, Number, IsTest)
            VALUES
                (@RefId, @WorkflowDefinitionId, @WorkflowRefId, @WorkflowVersion, @TriggerNodeId, @CorrelationKey, N'Running', @StartedAt, @CreditBudget, @TriggerStartKey, @ParentRunRefId, @Number, @RunIsTest);

            SET @RunId = SCOPE_IDENTITY();

            INSERT INTO dbo.RunCounters (RunId, ActiveBranchCount, CreditsConsumed, BudgetRunId, BudgetCeiling, HasUnposted, CreditAllowance, CreditLease)
            VALUES (@RunId, 1, @StartCost, @BudgetRunId, @BudgetCeiling, CASE WHEN @StartCost <> 0 THEN 1 ELSE 0 END, @RunCreditAllowance, @RunCreditLease);

            INSERT INTO dbo.Branches (RefId, RunId, NodeId, Status, LocalJson)
            VALUES (@BranchRefId, @RunId, @TriggerNodeId, N'Active', @BranchLocalJson);

            COMMIT TRANSACTION;
            SET @Created = 1;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0
                ROLLBACK TRANSACTION;

            -- Two attempts racing on the same key: the unique index lets one insert win, and the
            -- loser returns the winner's run.
            IF ERROR_NUMBER() NOT IN (2601, 2627) OR @TriggerStartKey IS NULL
                THROW;

            SET @RunId = NULL;
            SELECT @RunId = Id FROM dbo.Runs WHERE TriggerStartKey = @TriggerStartKey;
            IF @RunId IS NULL
                THROW;
        END CATCH
    END

    SELECT
        r.Id,
        r.RefId,
        r.WorkflowDefinitionId,
        r.WorkflowRefId,
        r.WorkflowVersion,
        r.TriggerNodeId,
        r.CorrelationKey,
        r.Status,
        r.StartedAt,
        r.CompletedAt,
        r.CancellationRequestedAt,
        r.CancellationReason,
        r.CreditBudget,
        r.CreatedAt,
        r.ParentRunRefId,
        r.OutputJson,
        r.Number,
        r.IsTest,
        rb.Id AS RootBranchId,
        @Created AS Created
    FROM dbo.Runs r
    CROSS APPLY (SELECT TOP (1) b.Id
                   FROM dbo.Branches b
                  WHERE b.RunId = r.Id
                    AND b.ParentBranchId IS NULL
                  ORDER BY b.Id) rb
    WHERE r.Id = @RunId;
END;
GO

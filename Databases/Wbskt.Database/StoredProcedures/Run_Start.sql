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
    @MaxActiveRuns        INT = NULL,
    @ParentRunRefId       UNIQUEIDENTIFIER = NULL,
    @ActiveRunGuard       TINYINT = 0,
    -- A test run (see dbo.WorkflowDefinitions.Kind): at most one goes at a time per workflow (50029).
    @IsTest               BIT = 0
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
            -- just put the runs it found. Taken before the workspace lock below, always in that order.
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

            -- The workspace's ceiling on runs going at once (Running, Failing or Cancelling, as in
            -- IX_Runs_WorkflowRefId_CorrelationKey_Active). Counted under an application lock per
            -- workspace, so concurrent starts one short of the ceiling cannot all pass the count.
            -- NULL is no ceiling. The wait for the lock is bounded: a start that cannot get it in a
            -- few seconds is refused as though the workspace were full, rather than holding an inbound
            -- request (and a pooled connection) for as long as the workspace stays contended.
            IF @MaxActiveRuns IS NOT NULL AND @WorkspaceId IS NOT NULL
            BEGIN
                DECLARE @Resource NVARCHAR(255) = CONCAT(N'run-ceiling:', @WorkspaceId);
                DECLARE @LockResult INT;
                EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
                IF @LockResult < 0
                    THROW 50026, 'The workspace is too busy to start another run.', 1;

                -- IX_Runs_WorkflowDefinitionId_Active keeps this to the runs still going.
                IF (SELECT COUNT(*)
                    FROM dbo.Runs r
                    JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
                    WHERE d.WorkspaceId = @WorkspaceId
                      AND r.Status IN (N'Running', N'Failing', N'Cancelling')) >= @MaxActiveRuns
                    THROW 50026, 'The workspace has reached its limit of active runs.', 1;
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

            INSERT INTO dbo.RunCounters (RunId, ActiveBranchCount)
            VALUES (@RunId, 1);

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

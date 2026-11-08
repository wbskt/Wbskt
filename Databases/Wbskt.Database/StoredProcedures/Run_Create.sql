CREATE PROCEDURE dbo.Run_Create
    @RefId                UNIQUEIDENTIFIER,
    @WorkflowDefinitionId INT,
    @WorkflowRefId        UNIQUEIDENTIFIER,
    @WorkflowVersion      INT,
    @TriggerNodeId        UNIQUEIDENTIFIER,
    @CorrelationKey       NVARCHAR(400),
    @StartedAt            DATETIME2(3),
    @CreditBudget         DECIMAL(18,4),
    @ParentRunRefId       UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RunId INT;

    DECLARE @Number INT;

    BEGIN TRANSACTION;

    EXEC dbo.WorkflowRunSequence_Next @WorkflowRefId = @WorkflowRefId, @Number = @Number OUTPUT;

    INSERT INTO dbo.Runs
        (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, CorrelationKey, Status, StartedAt, CreditBudget, ParentRunRefId, Number)
    VALUES
        (@RefId, @WorkflowDefinitionId, @WorkflowRefId, @WorkflowVersion, @TriggerNodeId, @CorrelationKey, N'Running', @StartedAt, @CreditBudget, @ParentRunRefId, @Number);

    SET @RunId = SCOPE_IDENTITY();

    INSERT INTO dbo.RunCounters (RunId)
    VALUES (@RunId);

    COMMIT TRANSACTION;

    SELECT
        Id,
        RefId,
        WorkflowDefinitionId,
        WorkflowRefId,
        WorkflowVersion,
        TriggerNodeId,
        CorrelationKey,
        Status,
        StartedAt,
        CompletedAt,
        CancellationRequestedAt,
        CancellationReason,
        CreditBudget,
        CreatedAt,
        ParentRunRefId,
        OutputJson,
        Number
    FROM dbo.Runs
    WHERE Id = @RunId;
END;
GO

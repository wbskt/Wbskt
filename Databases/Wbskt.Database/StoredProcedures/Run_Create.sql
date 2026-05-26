CREATE PROCEDURE dbo.Run_Create
    @RefId                UNIQUEIDENTIFIER,
    @WorkflowDefinitionId INT,
    @WorkflowRefId        UNIQUEIDENTIFIER,
    @WorkflowVersion      INT,
    @TriggerNodeId        UNIQUEIDENTIFIER,
    @CorrelationKey       NVARCHAR(400),
    @StartedAt            DATETIME2(3),
    @CreditBudget         DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RunId INT;

    BEGIN TRANSACTION;

    INSERT INTO dbo.Runs
        (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, CorrelationKey, Status, StartedAt, CreditBudget)
    VALUES
        (@RefId, @WorkflowDefinitionId, @WorkflowRefId, @WorkflowVersion, @TriggerNodeId, @CorrelationKey, N'Running', @StartedAt, @CreditBudget);

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
        CreatedAt
    FROM dbo.Runs
    WHERE Id = @RunId;
END;
GO

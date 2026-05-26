CREATE PROCEDURE dbo.TriggerRegistration_Insert
    @WorkflowDefinitionId  INT,
    @WorkflowRefId         UNIQUEIDENTIFIER,
    @WorkflowVersion       INT,
    @TriggerNodeId         UNIQUEIDENTIFIER,
    @TriggerKind           NVARCHAR(64),
    @TriggerKey            NVARCHAR(400),
    @CorrelationExpression NVARCHAR(1000),
    @ConcurrencyPolicy     NVARCHAR(32),
    @FilterExpression      NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.TriggerRegistrations
        (WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, TriggerKind, TriggerKey, CorrelationExpression, ConcurrencyPolicy, FilterExpression)
    VALUES
        (@WorkflowDefinitionId, @WorkflowRefId, @WorkflowVersion, @TriggerNodeId, @TriggerKind, @TriggerKey, @CorrelationExpression, @ConcurrencyPolicy, @FilterExpression);

    DECLARE @NewId INT = SCOPE_IDENTITY();

    SELECT
        Id,
        WorkflowDefinitionId,
        WorkflowRefId,
        WorkflowVersion,
        TriggerNodeId,
        TriggerKind,
        TriggerKey,
        CorrelationExpression,
        ConcurrencyPolicy,
        FilterExpression,
        CreatedAt
    FROM dbo.TriggerRegistrations
    WHERE Id = @NewId;
END;
GO

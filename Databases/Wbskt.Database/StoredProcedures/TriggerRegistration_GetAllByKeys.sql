CREATE PROCEDURE dbo.TriggerRegistration_GetAllByKeys
    @TriggerKind VARCHAR(50),
    @KeysJson    NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

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
    WHERE TriggerKind = @TriggerKind
      AND TriggerKey IN (
          SELECT [value]
          FROM OPENJSON(@KeysJson)
      );
END;
GO

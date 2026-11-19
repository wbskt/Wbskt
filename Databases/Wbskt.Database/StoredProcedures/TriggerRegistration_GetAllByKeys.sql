CREATE PROCEDURE dbo.TriggerRegistration_GetAllByKeys
    @TriggerKind VARCHAR(50),
    @KeysJson    NVARCHAR(MAX),
    -- When set, only the registrations of that workspace's workflows: a sub-workflow step passes its
    -- own, since a manual trigger's key is the workflow ref alone.
    @WorkspaceId INT = NULL
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
        WebhookSecret,
        CreatedAt
    FROM dbo.TriggerRegistrations AS tr
    WHERE tr.TriggerKind = @TriggerKind
      AND tr.TriggerKey IN (
          SELECT [value]
          FROM OPENJSON(@KeysJson)
      )
      AND (
          @WorkspaceId IS NULL
          OR EXISTS (
              SELECT 1
              FROM dbo.WorkflowDefinitions AS wd
              WHERE wd.Id = tr.WorkflowDefinitionId
                AND wd.WorkspaceId = @WorkspaceId
          )
      );
END;
GO

-- Every shared variable of one workflow, by name, for the console's variable picker.
CREATE PROCEDURE dbo.SharedVariable_GetBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        WorkflowRefId,
        VarName,
        VarType,
        ValueJson,
        UpdatedAt,
        CreatedAt
    FROM dbo.SharedVariables
    WHERE WorkflowRefId = @WorkflowRefId
    ORDER BY VarName;
END;
GO

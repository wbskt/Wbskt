-- A workflow's shared variables, by name, for the console's variable picker: the first @Take, and in
-- @TotalCount how many there are, so a caller can tell a full list from a cut one.
CREATE PROCEDURE dbo.SharedVariable_GetBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @Take          INT,
    @TotalCount    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.SharedVariables
    WHERE WorkflowRefId = @WorkflowRefId;

    SELECT TOP (@Take)
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

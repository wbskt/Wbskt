CREATE PROCEDURE dbo.SharedVariable_GetBy_WorkflowRefId_Name
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100)
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
      AND VarName = @VarName;
END;
GO

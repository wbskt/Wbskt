CREATE PROCEDURE dbo.SharedVariable_Set
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @ValueJson     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.SharedVariables
    SET ValueJson = @ValueJson,
        UpdatedAt = SYSUTCDATETIME()
    WHERE WorkflowRefId = @WorkflowRefId
      AND VarName = @VarName;

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

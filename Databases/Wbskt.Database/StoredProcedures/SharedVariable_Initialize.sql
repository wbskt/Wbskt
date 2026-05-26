CREATE PROCEDURE dbo.SharedVariable_Initialize
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @VarType       NVARCHAR(16),
    @ValueJson     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.SharedVariables WHERE WorkflowRefId = @WorkflowRefId AND VarName = @VarName)
    BEGIN
        INSERT INTO dbo.SharedVariables (WorkflowRefId, VarName, VarType, ValueJson)
        VALUES (@WorkflowRefId, @VarName, @VarType, @ValueJson);
    END;

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

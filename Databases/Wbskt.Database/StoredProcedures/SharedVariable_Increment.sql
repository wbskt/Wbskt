CREATE PROCEDURE dbo.SharedVariable_Increment
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @Delta         BIGINT = 1
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.SharedVariables WITH (ROWLOCK)
    SET ValueJson = CAST(CAST(ValueJson AS BIGINT) + @Delta AS NVARCHAR(MAX)),
        UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.ValueJson
    WHERE WorkflowRefId = @WorkflowRefId
      AND VarName = @VarName
      AND VarType = N'Counter';
END;
GO

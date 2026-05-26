CREATE PROCEDURE dbo.SharedVariable_CompareAndSet
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @Expected      NVARCHAR(MAX),
    @NewValue      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.SharedVariables
    SET ValueJson = @NewValue,
        UpdatedAt = SYSUTCDATETIME()
    WHERE WorkflowRefId = @WorkflowRefId
      AND VarName = @VarName
      AND ValueJson = @Expected;

    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

CREATE PROCEDURE dbo.SharedVariable_Initialize
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @VarType       NVARCHAR(16),
    @ValueJson     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    INSERT INTO dbo.SharedVariables (WorkflowRefId, VarName, VarType, ValueJson)
    SELECT @WorkflowRefId, @VarName, @VarType, @ValueJson
     WHERE NOT EXISTS (
         SELECT 1
           FROM dbo.SharedVariables WITH (UPDLOCK, HOLDLOCK)
          WHERE WorkflowRefId = @WorkflowRefId AND VarName = @VarName
     );

    COMMIT TRAN;

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

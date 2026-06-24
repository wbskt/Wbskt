CREATE PROCEDURE dbo.SharedVariable_Initialize
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @VarType       NVARCHAR(16),
    @ValueJson     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Attempt insert directly. Under high concurrency, we avoid checking existence beforehand
    -- with range/key locks (UPDLOCK, HOLDLOCK) which cause severe SQL deadlocks.
    -- If another thread already inserted this variable, we catch the unique constraint error
    -- (2601/2627), ignore it, and proceed to SELECT the existing row.
    BEGIN TRY
        INSERT INTO dbo.SharedVariables (WorkflowRefId, VarName, VarType, ValueJson)
        VALUES (@WorkflowRefId, @VarName, @VarType, @ValueJson);
    END TRY
    BEGIN CATCH
        -- Suppress duplicate key violation errors (2601 = Unique Index, 2627 = Unique Constraint)
        IF ERROR_NUMBER() NOT IN (2601, 2627)
        BEGIN
            THROW;
        END
    END CATCH;

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

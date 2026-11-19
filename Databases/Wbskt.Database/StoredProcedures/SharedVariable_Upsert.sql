-- Sets a shared variable, creating it with @VarType when it does not exist yet: the workflow step's
-- Set, which is last-writer-wins. An UPDATE first, the common case; then an INSERT, and when another
-- writer created the row in between (2601/2627), the UPDATE again, so this write still lands after
-- theirs instead of being dropped as an Initialize would drop it.
CREATE PROCEDURE dbo.SharedVariable_Upsert
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @VarType       NVARCHAR(16),
    @ValueJson     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE dbo.SharedVariables WITH (ROWLOCK)
    SET ValueJson = @ValueJson,
        UpdatedAt = SYSUTCDATETIME()
    WHERE WorkflowRefId = @WorkflowRefId
      AND VarName = @VarName;

    IF @@ROWCOUNT = 0
    BEGIN
        BEGIN TRY
            INSERT INTO dbo.SharedVariables (WorkflowRefId, VarName, VarType, ValueJson)
            VALUES (@WorkflowRefId, @VarName, @VarType, @ValueJson);
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() NOT IN (2601, 2627)
            BEGIN
                THROW;
            END

            UPDATE dbo.SharedVariables WITH (ROWLOCK)
            SET ValueJson = @ValueJson,
                UpdatedAt = SYSUTCDATETIME()
            WHERE WorkflowRefId = @WorkflowRefId
              AND VarName = @VarName;
        END CATCH;
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

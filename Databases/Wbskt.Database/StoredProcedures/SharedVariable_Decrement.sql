-- Applies the step atomically and creates the counter at the step if it does not exist yet, so two
-- first Decrements on the same name both count. A variable that exists but does not hold an integer
-- counter (created by Set, or overwritten by Set) is refused with 50023 rather than failing the cast.
CREATE PROCEDURE dbo.SharedVariable_Decrement
    @WorkflowRefId UNIQUEIDENTIFIER,
    @VarName       NVARCHAR(100),
    @Delta         BIGINT = 1
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Result TABLE (ValueJson NVARCHAR(MAX) NOT NULL);

    UPDATE dbo.SharedVariables WITH (ROWLOCK)
    SET ValueJson = CAST(TRY_CAST(ValueJson AS BIGINT) - @Delta AS NVARCHAR(MAX)),
        UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.ValueJson INTO @Result (ValueJson)
    WHERE WorkflowRefId = @WorkflowRefId
      AND VarName = @VarName
      AND VarType = N'Counter'
      AND TRY_CAST(ValueJson AS BIGINT) IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM @Result)
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.SharedVariables WHERE WorkflowRefId = @WorkflowRefId AND VarName = @VarName)
            THROW 50023, 'Shared variable is not a counter.', 1;

        BEGIN TRY
            INSERT INTO dbo.SharedVariables (WorkflowRefId, VarName, VarType, ValueJson)
            OUTPUT inserted.ValueJson INTO @Result (ValueJson)
            VALUES (@WorkflowRefId, @VarName, N'Counter', CAST(-@Delta AS NVARCHAR(MAX)));
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() NOT IN (2601, 2627)
                THROW;

            -- Another first Decrement created it between the check and the insert; apply ours to theirs.
            UPDATE dbo.SharedVariables WITH (ROWLOCK)
            SET ValueJson = CAST(TRY_CAST(ValueJson AS BIGINT) - @Delta AS NVARCHAR(MAX)),
                UpdatedAt = SYSUTCDATETIME()
            OUTPUT inserted.ValueJson INTO @Result (ValueJson)
            WHERE WorkflowRefId = @WorkflowRefId
              AND VarName = @VarName
              AND VarType = N'Counter'
              AND TRY_CAST(ValueJson AS BIGINT) IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM @Result)
                THROW 50023, 'Shared variable is not a counter.', 1;
        END CATCH;
    END

    SELECT ValueJson FROM @Result;
END;
GO

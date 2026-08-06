CREATE PROCEDURE dbo.WorkflowDefinition_DeleteUnreferencedById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Compensation for a publish that failed partway: the definition row was inserted but its
    -- triggers could not be registered, so it must not survive as a version that can never fire.
    --
    -- Guarded, not a general-purpose delete. A definition with runs against it is history and is
    -- never removed - if any exist, this is a no-op and the caller is left to report the failure.
    -- The rowcount tells the caller which happened.
    DELETE FROM dbo.WorkflowDefinitions
    WHERE Id = @Id
      AND NOT EXISTS (SELECT 1 FROM dbo.Runs r WHERE r.WorkflowDefinitionId = @Id);

    SELECT @@ROWCOUNT;
END;
GO

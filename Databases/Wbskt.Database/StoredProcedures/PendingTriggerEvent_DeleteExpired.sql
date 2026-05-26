CREATE PROCEDURE dbo.PendingTriggerEvent_DeleteExpired
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Deleted TABLE
    (
        Id INT NOT NULL
    );

    ;WITH Targets AS
    (
        SELECT TOP (@BatchSize)
            Id
        FROM dbo.PendingTriggerEvents
        WHERE EnqueuedAt < @CutoffUtc
        ORDER BY Id
    )
    DELETE FROM dbo.PendingTriggerEvents
    OUTPUT DELETED.Id INTO @Deleted (Id)
    WHERE Id IN (SELECT Id FROM Targets);

    SELECT COUNT(*)
    FROM @Deleted;
END;
GO

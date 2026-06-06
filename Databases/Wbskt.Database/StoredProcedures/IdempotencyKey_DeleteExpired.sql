CREATE PROCEDURE dbo.IdempotencyKey_DeleteExpired
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
        FROM dbo.IdempotencyKeys
        WHERE CreatedAt < @CutoffUtc
        ORDER BY Id
    )
    DELETE FROM dbo.IdempotencyKeys
    OUTPUT DELETED.Id INTO @Deleted (Id)
    WHERE Id IN (SELECT Id FROM Targets);

    SELECT COUNT(*)
    FROM @Deleted;
END;
GO

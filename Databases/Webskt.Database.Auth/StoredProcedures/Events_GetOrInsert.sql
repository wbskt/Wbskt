CREATE PROCEDURE dbo.Events_GetOrInsert 
    @EventName NVARCHAR(100),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @Id = Id 
    FROM dbo.Events 
    WHERE EventName = @EventName;

    IF @Id IS NULL
    BEGIN
        INSERT INTO dbo.Events (EventName) 
        VALUES (@EventName);
        
        SET @Id = SCOPE_IDENTITY();
    END
END
GO

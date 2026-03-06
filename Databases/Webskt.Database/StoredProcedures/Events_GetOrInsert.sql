CREATE PROCEDURE dbo.Events_GetOrInsert 
    @EventName NVARCHAR(100),
    @EventCriticality SMALLINT,
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @Id = Id 
    FROM dbo.Events 
    WHERE EventName = @EventName;

    IF @Id IS NULL
    BEGIN
        INSERT INTO dbo.Events (EventName, EventCriticality) 
        VALUES (@EventName, @EventCriticality);
        
        SET @Id = SCOPE_IDENTITY();
    END
END
GO

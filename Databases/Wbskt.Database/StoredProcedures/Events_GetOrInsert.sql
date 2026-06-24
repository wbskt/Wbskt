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
        -- Attempt insert inside a TRY/CATCH. If another concurrent thread inserted this event 
        -- after our initial SELECT check, the unique key violation will be caught, suppressed,
        -- and we will retrieve the ID of the newly inserted row from the database.
        BEGIN TRY
            INSERT INTO dbo.Events (EventName, EventCriticality) 
            VALUES (@EventName, @EventCriticality);
            
            SET @Id = SCOPE_IDENTITY();
        END TRY
        BEGIN CATCH
            -- Suppress duplicate key violation errors (2601 = Unique Index, 2627 = Unique Constraint)
            IF ERROR_NUMBER() IN (2601, 2627)
            BEGIN
                SELECT @Id = Id 
                FROM dbo.Events 
                WHERE EventName = @EventName;
            END
            ELSE
            BEGIN
                THROW;
            END
        END CATCH
    END
END
GO

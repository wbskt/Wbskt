/*
    Procedure: dbo.Publishers_Insert
    Purpose: Inserts a new publisher and returns the generated Id.
    Parameters:
        - @Id INT OUTPUT: Returns the new publisher Id
        - @UserId INT: User Id (owner)
        - @PublisherRef UNIQUEIDENTIFIER: Unique publisher reference
        - @Name VARCHAR(100): Publisher name
    Returns: None (output parameter @Id is set)
    Author: Richard Joy
    Date: 2024-08-24
    Last Modified: 2024-08-24 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Publishers_Insert
    @Id INT OUTPUT,
    @UserId INT,
    @PublisherRef UNIQUEIDENTIFIER,
    @Name VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Publishers (
        UserId,
        PublisherRef,
        Name
    )
    VALUES (
        @UserId,
        @PublisherRef,
        @Name
    );
    SELECT @Id = SCOPE_IDENTITY();
END;

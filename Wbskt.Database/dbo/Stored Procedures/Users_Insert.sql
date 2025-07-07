/*
    Procedure: dbo.Users_Insert
    Purpose: Inserts a new user and returns the generated Id.
    Parameters:
        - @Id INT OUTPUT: Returns the new user Id
        - @Name VARCHAR(100): User name
        - @EmailId VARCHAR(100): User email address
        - @PasswordHash VARCHAR(512): Password hash
        - @PasswordSalt VARCHAR(50): Password salt
    Returns: None (output parameter @Id is set)
    Author: Richard Joy
    Date: 2024-08-24
    Last Modified: 2024-08-24 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Users_Insert
    @Id INT OUTPUT,
    @Name VARCHAR(100),
    @EmailId VARCHAR(100),
    @PasswordHash VARCHAR(512),
    @PasswordSalt VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Users (
        Name,
        EmailId,
        PasswordHash,
        PasswordSalt
    )
    VALUES (
        @Name,
        @EmailId,
        @PasswordHash,
        @PasswordSalt
    );
    SELECT @Id = SCOPE_IDENTITY();
END;

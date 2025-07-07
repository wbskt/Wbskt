/*
    Procedure: dbo.Clients_Upsert
    Purpose: Inserts a new client or updates an existing client based on UniqueRef. Updates only Name, ServerId, and PolicyId for existing clients.
    Parameters:
        - @Id INT OUTPUT: Returns the client Id (inserted or updated)
        - @Name VARCHAR(100): Client name
        - @UniqueRef UNIQUEIDENTIFIER: Unique client reference
        - @UserId INT: User Id (owner)
        - @ServerId INT: Assigned server Id
        - @PolicyId INT: Enrollment policy Id
    Returns: None (output parameter @Id is set)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Clients_Upsert
    @Id INT OUTPUT,
    @Name VARCHAR(100),
    @UniqueRef UNIQUEIDENTIFIER,
    @UserId INT,
    @ServerId INT,
    @PolicyId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ExistingId INT;

    SELECT @ExistingId = Id
    FROM dbo.Clients
    WHERE UniqueRef = @UniqueRef;

    IF @ExistingId IS NOT NULL
    BEGIN
        -- Update only mutable fields
        UPDATE dbo.Clients
        SET Name = @Name,
            ServerId = @ServerId,
            PolicyId = @PolicyId
        WHERE Id = @ExistingId;

        SET @Id = @ExistingId;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.Clients
            (Name, ServerId, UniqueRef, UserId, PolicyId)
        VALUES
            (@Name, @ServerId, @UniqueRef, @UserId, @PolicyId);

        SET @Id = SCOPE_IDENTITY();
    END
END;

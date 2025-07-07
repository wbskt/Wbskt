/*
    Procedure: dbo.EnrollmentPolicies_Insert
    Purpose: Insert new enrollment policy with provided GUID.
    Parameters:
        - @Id INT OUTPUT: Returns the new policy Id
        - @UserId INT: User Id (owner)
        - @PolicyRef UNIQUEIDENTIFIER: Unique policy reference
        - @Name VARCHAR(100): Policy name
        - @PolicyType INT: Policy type
        - @MaxClients INT (optional): Maximum clients allowed
        - @ExpiryDate DATETIME (optional): Expiry date
    Returns: None (output parameter @Id is set)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.EnrollmentPolicies_Insert
    @Id INT OUTPUT,
    @UserId INT,
    @PolicyRef UNIQUEIDENTIFIER,
    @Name VARCHAR(100),
    @PolicyType INT,
    @MaxClients INT = NULL,
    @ExpiryDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EnrollmentPolicies
        (UserId, PolicyRef, Name, PolicyType, MaxClients, ExpiryDate, CurrentUsage, IsActive)
    VALUES
        (@UserId, @PolicyRef, @Name, @PolicyType, @MaxClients, @ExpiryDate, 0, 1);

    SELECT @Id = SCOPE_IDENTITY();
END; 
/*
    Procedure: dbo.RegistrationPolicies_Delete
    Purpose: Deletes a registration policy.
    Parameters:
        - @Id INT: The policy Id to delete
    Returns: None
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.RegistrationPolicies_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.RegistrationPolicies
    WHERE Id = @Id;
END;

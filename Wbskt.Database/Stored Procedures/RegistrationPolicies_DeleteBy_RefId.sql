/*
    Procedure: dbo.RegistrationPolicies_DeleteBy_RefId
    Purpose: Deletes a registration policy by its unique RefId.
    Parameters:
        - @RefId UNIQUEIDENTIFIER: The policy RefId to delete
    Returns: None
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.RegistrationPolicies_DeleteBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.RegistrationPolicies
    WHERE RefId = @RefId;
END;

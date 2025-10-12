/*
    Stored Procedure: dbo.RegistrationPolicies_GetAll_ByUserId
    Purpose: Retrieves all registration policies for a specific user.
    Parameters:
        - @UserId: INT, the ID of the user whose policies are to be retrieved.
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE [dbo].[RegistrationPolicies_GetAll_ByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [RefId],
        [Name],
        [UserId],
        [MaxClients],
        [Expiry],
        [Pin]
    FROM
        [dbo].[RegistrationPolicies]
    WHERE
        [UserId] = @UserId;
END
GO

/*
    Stored Procedure: dbo.RegistrationPolicies_GetAll
    Purpose: Retrieves all registration policies modified since the specified date.
    Parameters:
        - @LastModified: DATETIME2, the timestamp to query from.
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Gemini - Adapted for SqlDependency
*/
CREATE PROCEDURE [dbo].[RegistrationPolicies_GetAll]
    @LastModified DATETIME2
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
        [Pin],
        [LastModified]
    FROM
        [dbo].[RegistrationPolicies]
    WHERE
        [LastModified] >= @LastModified;
END
GO

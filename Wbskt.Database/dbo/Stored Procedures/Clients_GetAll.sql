/*
    Procedure: dbo.Clients_GetAll
    Purpose: Retrieves all clients modified since the specified date.
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Re-added LastModified for SqlDependency
*/
CREATE PROCEDURE [dbo].[Clients_GetAll]
    @LastModified DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [RefId],
        [UserId],
        [RegistrationPolicyId],
        [Name],
        [Active],
        [LastModified]
    FROM
        [dbo].[Clients]
    WHERE
        [LastModified] >= @LastModified;
END
GO

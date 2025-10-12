/*
    Procedure: dbo.Clients_GetAll
    Purpose: Retrieves all clients.
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Aligned with new schema
*/
CREATE PROCEDURE [dbo].[Clients_GetAll]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [RefId],
        [UserId],
        [RegistrationPolicyId],
        [Name],
        [Active]
    FROM
        [dbo].[Clients];
END
GO

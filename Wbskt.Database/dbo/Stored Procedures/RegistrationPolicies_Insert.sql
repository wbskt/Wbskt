/*
    Stored Procedure: dbo.RegistrationPolicies_Insert
    Purpose: Inserts a new registration policy.
    Parameters:
        - @RefId: UNIQUEIDENTIFIER, unique policy reference
        - @Name: NVARCHAR(100), policy name
        - @UserId: INT, foreign key to Users
        - @MaxClients: INT, max clients allowed (nullable)
        - @Expiry: DATETIME, expiry date (nullable)
        - @Pin: INT, policy pin
        - @Id: INT, OUTPUT, the ID of the new record
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Initial version
*/
CREATE PROCEDURE [dbo].[RegistrationPolicies_Insert]
    @RefId UNIQUEIDENTIFIER,
    @Name NVARCHAR(100),
    @UserId INT,
    @MaxClients INT = NULL,
    @Expiry DATETIME = NULL,
    @Pin INT,
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[RegistrationPolicies] ([RefId], [Name], [UserId], [MaxClients], [Expiry], [Pin])
    VALUES (@RefId, @Name, @UserId, @MaxClients, @Expiry, @Pin);

    SET @Id = SCOPE_IDENTITY();
END
GO

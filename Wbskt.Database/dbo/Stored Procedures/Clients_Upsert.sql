/*
    Procedure: dbo.Clients_Upsert
    Purpose: Inserts a new client or updates an existing one based on RefId.
    Parameters:
        - @RefId: UNIQUEIDENTIFIER, the client's public reference
        - @UserId: INT, the owner's user ID
        - @RegistrationPolicyId: INT, the policy ID
        - @Name: NVARCHAR(100), the client's optional name
        - @Active: BIT, the client's active status
        - @Id: INT OUTPUT, the ID of the inserted or updated client
    Author: Richard Joy
    Date: 2025-10-12
    Last Modified: 2025-10-12 by Richard Joy - Aligned with new schema
*/
CREATE PROCEDURE [dbo].[Clients_Upsert]
    @RefId UNIQUEIDENTIFIER,
    @UserId INT,
    @RegistrationPolicyId INT,
    @Name NVARCHAR(100) = NULL,
    @Active BIT,
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM [dbo].[Clients] WHERE [RefId] = @RefId)
    BEGIN
        -- Update existing client
        UPDATE [dbo].[Clients]
        SET
            [Name] = @Name,
            [Active] = @Active,
            [RegistrationPolicyId] = @RegistrationPolicyId -- Allow policy to be updated
        WHERE
            [RefId] = @RefId;

        SELECT @Id = [Id] FROM [dbo].[Clients] WHERE [RefId] = @RefId;
    END
    ELSE
    BEGIN
        -- Insert new client
        INSERT INTO [dbo].[Clients] ([RefId], [UserId], [RegistrationPolicyId], [Name], [Active])
        VALUES (@RefId, @UserId, @RegistrationPolicyId, @Name, @Active);

        SET @Id = SCOPE_IDENTITY();
    END
END
GO

CREATE PROCEDURE [dbo].[Credentials_Delete_ById]
    @Id INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Ensure a user can only delete their own credentials
    DELETE FROM [dbo].[Credentials]
    WHERE [Id] = @Id AND [UserId] = @UserId;
END

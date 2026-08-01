-- Enables or disables an account. Login and token refresh both reject inactive users, but the
-- caller is still responsible for revoking live refresh tokens when deactivating.
CREATE PROCEDURE dbo.User_SetActive
    @Id INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET IsActive = @IsActive
    WHERE Id = @Id;
END
GO

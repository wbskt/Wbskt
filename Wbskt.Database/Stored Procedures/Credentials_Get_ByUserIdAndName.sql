CREATE PROCEDURE [dbo].[Credentials_Get_ByUserIdAndName]
    @UserId INT,
    @Name NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [EncryptedCredentials]
    FROM [dbo].[Credentials]
    WHERE [UserId] = @UserId AND [Name] = @Name;
END

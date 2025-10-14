CREATE PROCEDURE [dbo].[Credentials_GetAll_ByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Note: We explicitly exclude the EncryptedCredentials column for security.
    SELECT [Id], [UserId], [IntegrationType], [Name], [LastModified]
    FROM [dbo].[Credentials]
    WHERE [UserId] = @UserId;
END

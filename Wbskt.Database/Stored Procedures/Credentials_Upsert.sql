CREATE PROCEDURE [dbo].[Credentials_Upsert]
    @UserId INT,
    @IntegrationType VARCHAR(50),
    @Name NVARCHAR(100),
    @EncryptedCredentials NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM [dbo].[Credentials] WHERE [UserId] = @UserId AND [Name] = @Name)
    BEGIN
        -- Update existing credential
        UPDATE [dbo].[Credentials]
        SET [IntegrationType] = @IntegrationType,
            [EncryptedCredentials] = @EncryptedCredentials,
            [LastModified] = GETUTCDATE()
        WHERE [UserId] = @UserId AND [Name] = @Name;
    END
    ELSE
    BEGIN
        -- Insert new credential
        INSERT INTO [dbo].[Credentials] ([UserId], [IntegrationType], [Name], [EncryptedCredentials])
        VALUES (@UserId, @IntegrationType, @Name, @EncryptedCredentials);
    END
END

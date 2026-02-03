CREATE PROCEDURE [dbo].[sp_GetUserByEmail]
    @Email NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, Username, Email, PasswordHash, IsActive
    FROM [dbo].[Users]
    WHERE Email = @Email;
END

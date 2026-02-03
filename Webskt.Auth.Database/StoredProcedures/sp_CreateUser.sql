CREATE PROCEDURE [dbo].[sp_CreateUser]
    @Username NVARCHAR(50),
    @Email NVARCHAR(100),
    @PasswordHash NVARCHAR(255),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO [dbo].[Users] (Username, Email, PasswordHash)
    VALUES (@Username, @Email, @PasswordHash);
    
    SET @Id = SCOPE_IDENTITY();
END

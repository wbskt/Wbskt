CREATE PROCEDURE [dbo].[sp_GetUserById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT Id, Username, Email, PasswordHash, IsActive
    FROM [dbo].[Users]
    WHERE Id = @Id;
END

CREATE PROCEDURE dbo.User_GetBy_Email
    @Email NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id, 
        RefId,
        Username, 
        Email, 
        PasswordHash, 
        IsActive
    FROM dbo.Users
    WHERE Email = @Email;
END
GO

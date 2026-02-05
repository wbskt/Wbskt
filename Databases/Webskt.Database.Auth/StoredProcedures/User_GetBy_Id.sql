CREATE PROCEDURE dbo.User_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id, 
        Username, 
        Email, 
        PasswordHash, 
        IsActive
    FROM dbo.Users
    WHERE Id = @Id;
END

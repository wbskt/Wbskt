CREATE PROCEDURE dbo.User_GetBy_Id
    @Id INT
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
    WHERE Id = @Id;
END
GO

CREATE PROCEDURE dbo.Role_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id, 
        Name, 
        Description
    FROM dbo.Roles;
END

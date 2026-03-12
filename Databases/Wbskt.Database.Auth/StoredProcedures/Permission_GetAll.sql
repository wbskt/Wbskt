CREATE PROCEDURE dbo.Permission_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Slug, 
        Description
    FROM dbo.Permissions;
END
GO

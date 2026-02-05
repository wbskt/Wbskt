CREATE PROCEDURE dbo.Group_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id, 
        Name, 
        ParentGroupId
    FROM dbo.Groups;
END

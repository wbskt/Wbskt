CREATE PROCEDURE [dbo].[sp_CreateRole]
    @Name NVARCHAR(100),
    @Description NVARCHAR(255)
AS
BEGIN
    INSERT INTO [dbo].[Roles] (Name, Description)
    VALUES (@Name, @Description);
END

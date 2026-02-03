CREATE PROCEDURE [dbo].[sp_CreatePermission]
    @Slug NVARCHAR(100),
    @Description NVARCHAR(255)
AS
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE Slug = @Slug)
    BEGIN
        INSERT INTO [dbo].[Permissions] (Slug, Description)
        VALUES (@Slug, @Description);
    END
END

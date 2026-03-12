CREATE PROCEDURE dbo.Permission_Create
    @Slug NVARCHAR(100),
    @Description NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = @Slug)
    BEGIN
    SET NOCOUNT ON;

INSERT INTO dbo.Permissions (
            Slug, 
            Description
        )
        VALUES (
            @Slug, 
            @Description
        );
    END
END
GO

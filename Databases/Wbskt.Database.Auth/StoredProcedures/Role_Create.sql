CREATE PROCEDURE dbo.Role_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(255),
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

INSERT INTO dbo.Roles (
        TenantId,
        Name,
        Description
    )
    VALUES (
        @TenantId,
        @Name,
        @Description
    );
END
GO

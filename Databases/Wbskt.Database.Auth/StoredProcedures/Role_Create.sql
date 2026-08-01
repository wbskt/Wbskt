CREATE PROCEDURE dbo.Role_Create
    @Name NVARCHAR(100),
    @Description NVARCHAR(255),
    @TenantId INT,
    @RefId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @RefId = NEWID();

    INSERT INTO dbo.Roles (
        RefId,
        TenantId,
        Name,
        Description
    )
    VALUES (
        @RefId,
        @TenantId,
        @Name,
        @Description
    );
END
GO

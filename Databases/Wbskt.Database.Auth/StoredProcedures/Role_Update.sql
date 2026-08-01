CREATE PROCEDURE dbo.Role_Update
    @Id INT,
    @TenantId INT,
    @Name NVARCHAR(100),
    @Description NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        THROW 50001, 'Role does not belong to the specified tenant.', 1;
    END

    UPDATE dbo.Roles
    SET Name = @Name,
        Description = @Description
    WHERE Id = @Id;
END
GO

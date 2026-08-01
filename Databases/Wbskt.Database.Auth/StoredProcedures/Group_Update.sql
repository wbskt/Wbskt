CREATE PROCEDURE dbo.Group_Update
    @Id INT,
    @TenantId INT,
    @Name NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Groups WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        THROW 50003, 'Group does not belong to the specified tenant.', 1;
    END

    UPDATE dbo.Groups
    SET Name = @Name
    WHERE Id = @Id;
END
GO

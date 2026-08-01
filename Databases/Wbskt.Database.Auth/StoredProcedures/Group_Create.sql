CREATE PROCEDURE dbo.Group_Create
    @Name NVARCHAR(100),
    @TenantId INT,
    @ParentGroupId INT = NULL,
    @RefId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @ParentGroupId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Groups WHERE Id = @ParentGroupId AND TenantId = @TenantId)
    BEGIN
        THROW 50003, 'Parent group does not belong to the specified tenant.', 1;
    END

    SET @RefId = NEWID();

    INSERT INTO dbo.Groups (
        RefId,
        TenantId,
        Name,
        ParentGroupId
    )
    VALUES (
        @RefId,
        @TenantId,
        @Name,
        @ParentGroupId
    );
END
GO

CREATE PROCEDURE dbo.Group_Create
    @Name NVARCHAR(100),
    @TenantId INT,
    @ParentGroupId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @ParentGroupId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Groups WHERE Id = @ParentGroupId AND TenantId = @TenantId)
    BEGIN
        THROW 50003, 'Parent group does not belong to the specified tenant.', 1;
    END

INSERT INTO dbo.Groups (
        TenantId,
        Name,
        ParentGroupId
    )
    VALUES (
        @TenantId,
        @Name,
        @ParentGroupId
    );
END
GO

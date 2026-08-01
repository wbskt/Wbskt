-- Refuses to delete a group that still has children, rather than reparenting or cascading them.
-- Silently moving a subtree would change which roles its members inherit.
CREATE PROCEDURE dbo.Group_Delete
    @Id INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Groups WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        THROW 50003, 'Group does not belong to the specified tenant.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Groups WHERE ParentGroupId = @Id)
    BEGIN
        THROW 50005, 'Group has child groups and cannot be deleted.', 1;
    END

    BEGIN TRANSACTION;

    DELETE FROM dbo.UserGroups WHERE GroupId = @Id;
    DELETE FROM dbo.GroupRoles WHERE GroupId = @Id;
    DELETE FROM dbo.Groups WHERE Id = @Id;

    COMMIT TRANSACTION;
END
GO

-- Refuses to delete a group that still has children, rather than reparenting or cascading them.
-- Silently moving a subtree would change which roles its members inherit. The checks run in the
-- delete's transaction holding the group's row, so a child created at the same time (whose foreign key
-- has to read that row) either lands first and is seen, or waits and then fails on the missing parent.
CREATE PROCEDURE dbo.Group_Delete
    @Id INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.Groups WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND TenantId = @TenantId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50003, 'Group does not belong to the specified tenant.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.Groups WITH (UPDLOCK, HOLDLOCK) WHERE ParentGroupId = @Id)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50005, 'Group has child groups and cannot be deleted.', 1;
    END

    DELETE FROM dbo.UserGroups WHERE GroupId = @Id;
    DELETE FROM dbo.GroupRoles WHERE GroupId = @Id;
    DELETE FROM dbo.Groups WHERE Id = @Id;

    COMMIT TRANSACTION;
END
GO

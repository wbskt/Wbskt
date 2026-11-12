-- The names of the tenants that would make dbo.User_Delete refuse (50018), read without changing
-- anything. Asked before an account deletion cancels its subscriptions, so a deletion that is going to
-- be refused does not cancel them first. dbo.User_Delete checks again under its locks.
CREATE PROCEDURE dbo.User_GetDeletionBlockers
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Name
    FROM dbo.User_DeletionBlockers(@UserId)
    ORDER BY Name;
END
GO

-- Hands a workspace to another member of its tenant, through dbo.WorkspaceOwner_Grant (membership and
-- the Admin role there). It queues no event: the host publishes WorkspaceOwnershipTransferredEvent
-- itself, naming who made the transfer, from the previous owner's reference this returns. The previous
-- owner stays a member; removing them is a separate, ordinary member removal.
-- A suspended member cannot act in the tenant, so is refused like someone outside it.
CREATE PROCEDURE dbo.Workspace_SetOwner
    @WorkspaceId INT,
    @UserId INT,
    @PreviousOwnerRefId UNIQUEIDENTIFIER = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Workspaces WITH (UPDLOCK, HOLDLOCK) WHERE Id = @WorkspaceId);

    IF @TenantId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50004, 'Workspace does not exist.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId AND IsSuspended = 0)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50009, 'User is not a member of the tenant that owns this workspace.', 1;
    END

    SET @PreviousOwnerRefId = (
        SELECT U.RefId
        FROM dbo.Workspaces W
        INNER JOIN dbo.Users U ON U.Id = W.OwnerUserId
        WHERE W.Id = @WorkspaceId);

    EXEC dbo.WorkspaceOwner_Grant @WorkspaceId = @WorkspaceId, @UserId = @UserId, @TenantId = @TenantId, @Announce = 0;

    COMMIT TRANSACTION;
END
GO

-- Deletes a policy that no longer has clients. Message templates pinned to it are unpinned, so they
-- stay in the workspace for every device; its event-log rows stay, under its RefId.
--
-- Scoped by workspace. Returns one row: ClientCount, the clients still enrolled through it (any
-- status), and Deleted. A policy with clients is left as it is (Deleted 0, ClientCount > 0); one that
-- is not in @WorkspaceId returns Deleted 0, ClientCount 0. The policy row is locked first, so a device
-- registering with its PIN either lands before the count (and refuses the delete) or fails after it.
CREATE PROCEDURE dbo.RegistrationPolicy_Delete
    @Id          INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND WorkspaceId = @WorkspaceId)
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(0 AS BIT) AS Deleted, 0 AS ClientCount;
        RETURN;
    END

    DECLARE @ClientCount INT = (SELECT COUNT(*) FROM dbo.Clients WITH (HOLDLOCK) WHERE PolicyId = @Id);
    IF @ClientCount > 0
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(0 AS BIT) AS Deleted, @ClientCount AS ClientCount;
        RETURN;
    END

    UPDATE dbo.MessageTemplates
    SET PolicyId = NULL, UpdatedAt = SYSUTCDATETIME()
    WHERE PolicyId = @Id;

    DELETE FROM dbo.RegistrationPolicies WHERE Id = @Id AND WorkspaceId = @WorkspaceId;

    COMMIT TRANSACTION;
    SELECT CAST(1 AS BIT) AS Deleted, 0 AS ClientCount;
END
GO

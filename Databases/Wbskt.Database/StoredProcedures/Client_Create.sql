-- MaxClients is enforced here, under an update lock on the policy row, as well as by the caller's
-- earlier count. The count alone is check-then-insert: concurrent registrations against a policy one
-- short of its limit all pass it and all insert. The lock serialises registrations per policy, so
-- the count below sees every client committed before this one.
--
-- @MaxWorkspaceClients is the workspace's ceiling across all its policies: only registered clients
-- count, so it is checked only when the new client is created registered (auto-approval). A pending
-- client counts once it is approved, which Client_UpdateStatuses checks under the same lock. NULL is
-- no ceiling. Counted under an application lock per workspace, for the same reason as the policy lock.
CREATE PROCEDURE dbo.Client_Create
    @WorkspaceId INT,
    @PolicyId INT,
    @Name NVARCHAR(100),
    @SecretHash VARBINARY(32),
    @Status TINYINT,
    @Id INT OUTPUT,
    @RefId UNIQUEIDENTIFIER OUTPUT,
    @MaxWorkspaceClients INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @MaxClients INT;

    SELECT @MaxClients = MaxClients
    FROM dbo.RegistrationPolicies WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @PolicyId;

    -- Same rule as Client_GetCountBy_PolicyId: only registered clients count against the limit.
    IF @MaxClients IS NOT NULL
       AND (SELECT COUNT(*) FROM dbo.Clients WHERE PolicyId = @PolicyId AND Status = 1) >= @MaxClients
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50020, 'Policy registration limit reached.', 1;
    END

    IF @MaxWorkspaceClients IS NOT NULL AND @Status = 1
    BEGIN
        DECLARE @Resource NVARCHAR(255) = CONCAT(N'client-ceiling:', @WorkspaceId);
        EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';

        IF (SELECT COUNT(*) FROM dbo.Clients WHERE WorkspaceId = @WorkspaceId AND Status = 1) >= @MaxWorkspaceClients
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50025, 'The workspace has reached its client limit.', 1;
        END
    END

    INSERT INTO dbo.Clients (
        WorkspaceId,
        PolicyId,
        Name,
        SecretHash,
        Status
    )
    VALUES (
        @WorkspaceId,
        @PolicyId,
        @Name,
        @SecretHash,
        @Status
    );

    SELECT 
        @Id = Id,
        @RefId = RefId
    FROM dbo.Clients
    WHERE Id = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END
GO

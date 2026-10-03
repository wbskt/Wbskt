-- MaxClients is enforced here, under an update lock on the policy row, as well as by the caller's
-- earlier count. The count alone is check-then-insert: concurrent registrations against a policy one
-- short of its limit all pass it and all insert. The lock serialises registrations per policy, so
-- the count below sees every client committed before this one.
CREATE PROCEDURE dbo.Client_Create
    @WorkspaceId INT,
    @PolicyId INT,
    @Name NVARCHAR(100),
    @SecretHash VARBINARY(32),
    @Status TINYINT,
    @Id INT OUTPUT,
    @RefId UNIQUEIDENTIFIER OUTPUT
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

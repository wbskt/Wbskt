-- Approving a client (Status 1) is checked against the policy's MaxClients here, under the same
-- update lock on the policy row that Client_Create takes. The caller's count alone is
-- check-then-update: two approvals against a policy one short of its limit, or one bulk approval
-- racing another, would both pass it and both commit.
CREATE PROCEDURE dbo.Client_UpdateStatus
    @Id INT,
    @Status TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF @Status = 1
    BEGIN
        DECLARE @PolicyId INT, @MaxClients INT;

        SELECT @PolicyId = c.PolicyId
        FROM dbo.Clients c
        WHERE c.Id = @Id AND c.Status <> 1;

        IF @PolicyId IS NOT NULL
        BEGIN
            SELECT @MaxClients = MaxClients
            FROM dbo.RegistrationPolicies WITH (UPDLOCK, ROWLOCK)
            WHERE Id = @PolicyId;

            IF @MaxClients IS NOT NULL
               AND (SELECT COUNT(*) FROM dbo.Clients WHERE PolicyId = @PolicyId AND Status = 1) >= @MaxClients
            BEGIN
                ROLLBACK TRANSACTION;
                THROW 50020, 'Policy registration limit reached.', 1;
            END
        END
    END

    UPDATE dbo.Clients
    SET Status = @Status
    WHERE Id = @Id;

    COMMIT TRANSACTION;
END
GO

-- Stops what is going in a workspace whose account Wbskt support has frozen: its pending and registered
-- clients are revoked, and its runs still in progress are reported for the caller to cancel. Nothing is
-- disabled, deprecated or deleted, so lifting the freeze puts the workspace back as it was, apart from
-- the clients, which have to be approved again. New runs and enrolments are refused by dbo.Run_Start and
-- dbo.Client_Create while dbo.WorkspaceAccounts.FrozenAt is set, which the caller copies first.
--
-- Idempotent: a client already revoked is not reported again, and a run is reported for as long as it
-- is still running, so a cancel that failed to send goes out next time.
--
-- Returns one row per thing the caller must still act on outside the database:
--   Kind 'client' - a client revoked by this call; announce it so socket hosts drop the connection.
--   Kind 'run'    - a run still in progress; request its cancellation.
CREATE PROCEDURE dbo.Workspace_Freeze
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Revoked TABLE (ClientId INT, ClientRefId UNIQUEIDENTIFIER, PolicyId INT);

    BEGIN TRANSACTION;

    UPDATE dbo.Clients
    SET Status = 2 -- Revoked
    OUTPUT INSERTED.Id, INSERTED.RefId, INSERTED.PolicyId INTO @Revoked
    WHERE WorkspaceId = @WorkspaceId
      AND Status IN (0, 1); -- Pending, Registered; Revoked and Rejected already cannot connect

    COMMIT TRANSACTION;

    SELECT N'client' AS Kind, CAST(R.ClientId AS BIGINT) AS Id, R.ClientRefId AS RefId, R.PolicyId, P.RefId AS PolicyRefId
    FROM @Revoked R
    INNER JOIN dbo.RegistrationPolicies P ON P.Id = R.PolicyId

    UNION ALL

    SELECT N'run', CAST(RU.Id AS BIGINT), RU.RefId, NULL, NULL
    FROM dbo.Runs RU
    WHERE RU.WorkspaceId = @WorkspaceId
      AND RU.Status IN (N'Running', N'Failing');
END
GO

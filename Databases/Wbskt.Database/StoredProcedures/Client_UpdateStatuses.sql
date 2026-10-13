-- Sets one status on a list of clients in one call: the single-client status endpoint and bulk
-- approve both come here. Returns a row per input, in input order, saying what happened to it:
--   0 updated, 1 already had the status, 2 no such client, 3 another workspace's client,
--   4 not approved because its policy is full, 5 not approved because the workspace is full.
--
-- Approving (Status 1) is checked against each policy's MaxClients under an update lock on the
-- policy row, the same lock Client_Create takes, so racing approvals cannot overfill a policy.
-- When a batch asks for more places than a policy has left, the earliest in the list get them.
--
-- It is then checked against @MaxWorkspaceClients, the workspace's ceiling on registered clients
-- across all its policies (NULL is no ceiling), under the application lock Client_Create takes for
-- it. Both procedures take the policy lock first and the workspace lock second. The earliest in the
-- list that are still approvable get the workspace's remaining places.
CREATE PROCEDURE dbo.Client_UpdateStatuses
    @WorkspaceId INT,
    @Status TINYINT,
    @Clients dbo.OrderedRefIdTableType READONLY,
    @MaxWorkspaceClients INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Work TABLE
    (
        Ordinal     INT              NOT NULL PRIMARY KEY,
        RefId       UNIQUEIDENTIFIER NOT NULL,
        Id          INT              NULL,
        WorkspaceId INT              NULL,
        PolicyId    INT              NULL,
        OldStatus   TINYINT          NULL,
        Outcome     TINYINT          NOT NULL DEFAULT 0
    );

    BEGIN TRANSACTION;

    -- Locked so the old status reported back (which decides token cutoffs) is the one replaced.
    INSERT INTO @Work (Ordinal, RefId, Id, WorkspaceId, PolicyId, OldStatus)
    SELECT r.Ordinal, r.RefId, c.Id, c.WorkspaceId, c.PolicyId, c.Status
    FROM @Clients r
    LEFT JOIN dbo.Clients c WITH (UPDLOCK, ROWLOCK) ON c.RefId = r.RefId;

    UPDATE @Work SET Outcome = 2 WHERE Id IS NULL;
    UPDATE @Work SET Outcome = 3 WHERE Outcome = 0 AND WorkspaceId <> @WorkspaceId;
    UPDATE @Work SET Outcome = 1 WHERE Outcome = 0 AND OldStatus = @Status;

    IF @Status = 1 AND EXISTS (SELECT 1 FROM @Work WHERE Outcome = 0)
    BEGIN
        DECLARE @Policies TABLE (PolicyId INT NOT NULL PRIMARY KEY, MaxClients INT NULL, Remaining INT NULL);

        INSERT INTO @Policies (PolicyId, MaxClients)
        SELECT p.Id, p.MaxClients
        FROM dbo.RegistrationPolicies p WITH (UPDLOCK, ROWLOCK)
        WHERE p.Id IN (SELECT PolicyId FROM @Work WHERE Outcome = 0);

        UPDATE pol
        SET Remaining = pol.MaxClients - (SELECT COUNT(*) FROM dbo.Clients c WHERE c.PolicyId = pol.PolicyId AND c.Status = 1)
        FROM @Policies pol
        WHERE pol.MaxClients IS NOT NULL;

        WITH Queue AS
        (
            SELECT Outcome, PolicyId, ROW_NUMBER() OVER (PARTITION BY PolicyId ORDER BY Ordinal) AS Place
            FROM @Work
            WHERE Outcome = 0
        )
        UPDATE q
        SET Outcome = 4
        FROM Queue q
        INNER JOIN @Policies pol ON pol.PolicyId = q.PolicyId
        WHERE pol.Remaining IS NOT NULL AND q.Place > pol.Remaining;

        IF @MaxWorkspaceClients IS NOT NULL AND EXISTS (SELECT 1 FROM @Work WHERE Outcome = 0)
        BEGIN
            DECLARE @Resource NVARCHAR(255) = CONCAT(N'client-ceiling:', @WorkspaceId);
            EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';

            -- Every row still at outcome 0 is in this workspace and not yet registered, so none of
            -- them is in this count.
            DECLARE @WorkspaceRemaining INT = @MaxWorkspaceClients
                - (SELECT COUNT(*) FROM dbo.Clients WHERE WorkspaceId = @WorkspaceId AND Status = 1);

            WITH Queue AS
            (
                SELECT Outcome, ROW_NUMBER() OVER (ORDER BY Ordinal) AS Place
                FROM @Work
                WHERE Outcome = 0
            )
            UPDATE Queue
            SET Outcome = 5
            WHERE Place > @WorkspaceRemaining;
        END
    END

    UPDATE c
    SET Status = @Status
    FROM dbo.Clients c
    INNER JOIN @Work w ON w.Id = c.Id
    WHERE w.Outcome = 0;

    COMMIT TRANSACTION;

    SELECT w.Ordinal, w.RefId, w.Id, w.PolicyId, p.RefId AS PolicyRefId, w.OldStatus, w.Outcome
    FROM @Work w
    LEFT JOIN dbo.RegistrationPolicies p ON p.Id = w.PolicyId
    ORDER BY w.Ordinal;
END
GO

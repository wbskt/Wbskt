CREATE PROCEDURE dbo.ClientPresenceCheck_LeaseDue
    @LeaseSec INT = 15,
    @Batch    INT = 64
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Leased TABLE
    (
        Id          INT              NOT NULL PRIMARY KEY,
        TriggerKey  NVARCHAR(400)    NOT NULL,
        ClientRefId UNIQUEIDENTIFIER NOT NULL,
        State       NVARCHAR(16)     NOT NULL,
        ChangedAt   DATETIME2(3)     NOT NULL,
        DueAt       DATETIME2(3)     NOT NULL
    );

    UPDATE TOP (@Batch) dbo.ClientPresenceChecks WITH (UPDLOCK, READPAST)
    SET LeasedUntil = DATEADD(SECOND, @LeaseSec, SYSUTCDATETIME())
    OUTPUT
        inserted.Id,
        inserted.TriggerKey,
        inserted.ClientRefId,
        inserted.State,
        inserted.ChangedAt,
        inserted.DueAt
    INTO @Leased
    WHERE DueAt <= SYSUTCDATETIME()
      AND (LeasedUntil IS NULL OR LeasedUntil < SYSUTCDATETIME());

    -- The client's presence as it stands now, read alongside the lease so the ticker can decide
    -- whether the change still holds. A deleted client comes back with ClientId NULL.
    SELECT
        L.Id,
        L.TriggerKey,
        L.ClientRefId,
        L.State,
        L.ChangedAt,
        L.DueAt,
        C.Id             AS ClientId,
        C.WorkspaceId    AS ClientWorkspaceId,
        C.IsConnected    AS ClientIsConnected,
        C.ConnectedAt    AS ClientConnectedAt,
        C.LastActivityAt AS ClientLastActivityAt
    FROM @Leased L
    LEFT JOIN dbo.Clients C ON C.RefId = L.ClientRefId;
END;
GO

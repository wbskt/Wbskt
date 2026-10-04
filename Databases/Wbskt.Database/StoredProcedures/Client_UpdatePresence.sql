CREATE PROCEDURE dbo.Client_UpdatePresence
    @Id INT,
    @IsConnected BIT,
    @LastActivityAt DATETIME2(3),
    @HostId NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Connect and disconnect events are consumed independently, so a client that drops right
    -- after connecting can have its disconnect applied first. Each change carries the time the
    -- socket host saw it; a change older than the one already recorded is stale and ignored.
    -- On a tie the disconnect wins, so the client never ends up shown online after it left.
    IF @IsConnected = 1
    BEGIN
        UPDATE dbo.Clients
        SET
            IsConnected = 1,
            LastActivityAt = @LastActivityAt,
            -- ConnectedAt anchors uptime: set on connect, cleared on disconnect
            ConnectedAt = @LastActivityAt,
            ConnectedHostId = @HostId
        WHERE Id = @Id
            AND (LastActivityAt IS NULL OR LastActivityAt < @LastActivityAt);
    END
    ELSE
    BEGIN
        -- Only the host that currently holds the connection may clear presence; a late
        -- disconnect from a superseded host (see ConnectionSupersededHandler) is a no-op.
        -- No holder yet means the matching connect has not been applied (or never will be).
        UPDATE dbo.Clients
        SET
            IsConnected = 0,
            LastActivityAt = @LastActivityAt,
            ConnectedAt = NULL,
            ConnectedHostId = NULL
        WHERE Id = @Id
            AND (LastActivityAt IS NULL OR LastActivityAt <= @LastActivityAt)
            AND (@HostId IS NULL OR ConnectedHostId IS NULL OR ConnectedHostId = @HostId);
    END
END
GO

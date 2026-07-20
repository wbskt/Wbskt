CREATE PROCEDURE dbo.Client_UpdatePresence
    @Id INT,
    @IsConnected BIT,
    @LastActivityAt DATETIME2(3),
    @HostId NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @IsConnected = 1
    BEGIN
        UPDATE dbo.Clients
        SET
            IsConnected = 1,
            LastActivityAt = @LastActivityAt,
            -- ConnectedAt anchors uptime: set on connect, cleared on disconnect
            ConnectedAt = @LastActivityAt,
            ConnectedHostId = @HostId
        WHERE Id = @Id;
    END
    ELSE
    BEGIN
        -- Only the host that currently holds the connection may clear presence; a late
        -- disconnect from a superseded host (see ConnectionSupersededHandler) is a no-op.
        UPDATE dbo.Clients
        SET
            IsConnected = 0,
            LastActivityAt = @LastActivityAt,
            ConnectedAt = NULL,
            ConnectedHostId = NULL
        WHERE Id = @Id
            AND (@HostId IS NULL OR ConnectedHostId = @HostId);
    END
END
GO

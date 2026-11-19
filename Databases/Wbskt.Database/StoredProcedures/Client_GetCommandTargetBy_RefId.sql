-- Where a command for this client goes: its workspace (the sender's must match) and the socket host
-- holding its connection and when that host saw it connect, and its status (only a registered client may have commands wait for it).
-- HostSilentSeconds is how long ago, by the database's clock, that host last sent a heartbeat; NULL
-- when it has none on record. A host that crashed still holds its clients' presence until the sweep,
-- and a command addressed to it would be lost, so the sender queues instead (#960).
-- No row when the client does not exist.
CREATE PROCEDURE dbo.Client_GetCommandTargetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        C.Id,
        C.WorkspaceId,
        C.IsConnected,
        C.ConnectedHostId,
        C.ConnectedAt,
        C.Status,
        DATEDIFF(SECOND, SH.LastSeenAt, SYSUTCDATETIME()) AS HostSilentSeconds
    FROM dbo.Clients AS C
    LEFT JOIN dbo.SocketHosts AS SH
        ON SH.HostId = C.ConnectedHostId
    WHERE C.RefId = @RefId;
END
GO

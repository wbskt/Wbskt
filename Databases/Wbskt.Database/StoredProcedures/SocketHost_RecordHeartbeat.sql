-- Records that a socket host is alive. The time is the database's own, so a socket host's clock
-- never decides whether it counts as alive.
--
-- When the host had been silent for longer than @SilentAfterSeconds (or had no beat on record), a
-- command sent to one of its clients meanwhile was queued rather than addressed to it (#960), and the
-- client, still connected, will not connect again to take it. So this returns the clients the host
-- holds that have commands waiting, for the caller to deliver. A command queued after this beat sees
-- the host live again and is delivered by the queue itself (ClientCommandQueue.QueueAsync).
CREATE PROCEDURE dbo.SocketHost_RecordHeartbeat
    @HostId             NVARCHAR(100),
    @SilentAfterSeconds INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @Previous TABLE (LastSeenAt DATETIME2(3) NOT NULL);

    UPDATE dbo.SocketHosts
    SET LastSeenAt = @Now
    OUTPUT DELETED.LastSeenAt INTO @Previous (LastSeenAt)
    WHERE HostId = @HostId;

    IF @@ROWCOUNT = 0
    BEGIN
        BEGIN TRY
            INSERT INTO dbo.SocketHosts (HostId, LastSeenAt)
            VALUES (@HostId, @Now);
        END TRY
        BEGIN CATCH
            -- Two beats of a new host raced to insert; the other one recorded it.
            IF ERROR_NUMBER() <> 2627
                THROW;
        END CATCH
    END

    IF @SilentAfterSeconds IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM @Previous WHERE DATEDIFF(SECOND, LastSeenAt, @Now) <= @SilentAfterSeconds)
    BEGIN
        SELECT
            C.Id AS ClientId,
            C.RefId AS ClientRefId,
            C.WorkspaceId
        FROM dbo.Clients C
        WHERE C.IsConnected = 1
          AND C.ConnectedHostId = @HostId
          AND EXISTS (SELECT 1 FROM dbo.QueuedCommands Q WHERE Q.ClientId = C.Id);
    END
END
GO

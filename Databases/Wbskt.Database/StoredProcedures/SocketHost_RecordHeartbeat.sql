-- Records that a socket host is alive. The time is the database's own, so a socket host's clock
-- never decides whether it counts as alive.
CREATE PROCEDURE dbo.SocketHost_RecordHeartbeat
    @HostId NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.SocketHosts
    SET LastSeenAt = SYSUTCDATETIME()
    WHERE HostId = @HostId;

    IF @@ROWCOUNT = 0
    BEGIN
        BEGIN TRY
            INSERT INTO dbo.SocketHosts (HostId, LastSeenAt)
            VALUES (@HostId, SYSUTCDATETIME());
        END TRY
        BEGIN CATCH
            -- Two beats of a new host raced to insert; the other one recorded it.
            IF ERROR_NUMBER() <> 2627
                THROW;
        END CATCH
    END
END
GO

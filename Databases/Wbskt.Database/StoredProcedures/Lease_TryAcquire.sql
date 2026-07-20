CREATE PROCEDURE dbo.Lease_TryAcquire
    @LeaseName  NVARCHAR(100),
    @HolderId   NVARCHAR(100),
    @TtlSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now       DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @NewExpiry DATETIME2(3) = DATEADD(SECOND, @TtlSeconds, @Now);

    -- Renewal by the current holder, or takeover of an expired/unheld lease.
    UPDATE dbo.Leases
    SET
        HolderId = @HolderId,
        ExpiresAt = @NewExpiry,
        AcquiredAt = CASE WHEN HolderId = @HolderId THEN AcquiredAt ELSE @Now END
    WHERE LeaseName = @LeaseName
        AND (ExpiresAt < @Now OR HolderId = @HolderId);

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT CAST(1 AS BIT) AS Acquired;
        RETURN;
    END

    -- No row yet for this lease name: first-ever acquisition. A concurrent first-acquirer racing
    -- here loses on the primary key and reports Acquired = 0 rather than an error.
    BEGIN TRY
        INSERT INTO dbo.Leases (LeaseName, HolderId, ExpiresAt, AcquiredAt)
        VALUES (@LeaseName, @HolderId, @NewExpiry, @Now);
        SELECT CAST(1 AS BIT) AS Acquired;
    END TRY
    BEGIN CATCH
        SELECT CAST(0 AS BIT) AS Acquired;
    END CATCH
END
GO

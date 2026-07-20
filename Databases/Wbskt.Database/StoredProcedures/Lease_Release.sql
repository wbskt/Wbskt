-- Only the current holder can release; a fenced-out former leader releasing late is a no-op
-- (it no longer matches HolderId once someone else has taken the lease over).
CREATE PROCEDURE dbo.Lease_Release
    @LeaseName NVARCHAR(100),
    @HolderId  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Leases
    SET ExpiresAt = SYSUTCDATETIME()
    WHERE LeaseName = @LeaseName
        AND HolderId = @HolderId;
END
GO

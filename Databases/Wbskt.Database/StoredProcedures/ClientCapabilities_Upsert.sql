-- Stores a client's capabilities report unless a newer one is already stored: reports are consumed
-- independently and retried, so a quick reconnect can deliver them out of order. @ReportedAt is when
-- the gateway received it. Returns Applied = 1 when it was stored.
CREATE PROCEDURE dbo.ClientCapabilities_Upsert
    @ClientId INT,
    @AgentName NVARCHAR(100),
    @AgentVersion NVARCHAR(50),
    @Platform NVARCHAR(100),
    @CapabilitiesJson NVARCHAR(MAX),
    @SystemJson NVARCHAR(1000) = NULL,
    @FirmwareVersion NVARCHAR(50) = NULL,
    @ReportedAt DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ClientCapabilities
    SET
        AgentName = @AgentName,
        AgentVersion = @AgentVersion,
        Platform = @Platform,
        CapabilitiesJson = @CapabilitiesJson,
        SystemJson = @SystemJson,
        FirmwareVersion = @FirmwareVersion,
        UpdatedAt = SYSUTCDATETIME(),
        ReportedAt = @ReportedAt
    WHERE ClientId = @ClientId
        AND (@ReportedAt IS NULL OR ReportedAt IS NULL OR ReportedAt <= @ReportedAt);

    IF @@ROWCOUNT > 0
    BEGIN
        SELECT CAST(1 AS BIT) AS Applied;
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.ClientCapabilities WHERE ClientId = @ClientId)
    BEGIN
        SELECT CAST(0 AS BIT) AS Applied;
        RETURN;
    END

    INSERT INTO dbo.ClientCapabilities (ClientId, AgentName, AgentVersion, Platform, CapabilitiesJson, SystemJson, FirmwareVersion, ReportedAt)
    VALUES (@ClientId, @AgentName, @AgentVersion, @Platform, @CapabilitiesJson, @SystemJson, @FirmwareVersion, @ReportedAt);
    SELECT CAST(1 AS BIT) AS Applied;
END
GO

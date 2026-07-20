CREATE PROCEDURE dbo.ClientCapabilities_Upsert
    @ClientId INT,
    @AgentName NVARCHAR(100),
    @AgentVersion NVARCHAR(50),
    @Platform NVARCHAR(100),
    @CapabilitiesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ClientCapabilities
    SET
        AgentName = @AgentName,
        AgentVersion = @AgentVersion,
        Platform = @Platform,
        CapabilitiesJson = @CapabilitiesJson,
        UpdatedAt = SYSUTCDATETIME()
    WHERE ClientId = @ClientId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.ClientCapabilities (ClientId, AgentName, AgentVersion, Platform, CapabilitiesJson)
        VALUES (@ClientId, @AgentName, @AgentVersion, @Platform, @CapabilitiesJson);
    END
END
GO

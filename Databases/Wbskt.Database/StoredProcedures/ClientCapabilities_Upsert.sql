CREATE PROCEDURE dbo.ClientCapabilities_Upsert
    @ClientId INT,
    @AgentName NVARCHAR(100),
    @AgentVersion NVARCHAR(50),
    @Platform NVARCHAR(100),
    @CapabilitiesJson NVARCHAR(MAX),
    @SystemJson NVARCHAR(1000) = NULL
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
        UpdatedAt = SYSUTCDATETIME()
    WHERE ClientId = @ClientId;

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.ClientCapabilities (ClientId, AgentName, AgentVersion, Platform, CapabilitiesJson, SystemJson)
        VALUES (@ClientId, @AgentName, @AgentVersion, @Platform, @CapabilitiesJson, @SystemJson);
    END
END
GO

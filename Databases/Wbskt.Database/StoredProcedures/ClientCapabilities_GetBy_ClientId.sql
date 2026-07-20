CREATE PROCEDURE dbo.ClientCapabilities_GetBy_ClientId
    @ClientId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ClientId,
        AgentName,
        AgentVersion,
        Platform,
        CapabilitiesJson,
        UpdatedAt
    FROM dbo.ClientCapabilities
    WHERE ClientId = @ClientId;
END
GO

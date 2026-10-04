-- Detail-page read: full client row + policy name + capability sidecar (LEFT JOIN:
-- capability columns are NULL until the client first reports capabilities).
CREATE PROCEDURE dbo.Client_GetDetailBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.Id,
        c.RefId,
        c.WorkspaceId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        p.Name AS PolicyName,
        c.Name,
        c.Status,
        c.IsConnected,
        c.ConnectedAt,
        c.LastActivityAt,
        c.LastRttMs,
        c.RttMeasuredAt,
        c.ConnectedHostId,
        c.CreatedAt,
        cap.AgentName,
        cap.AgentVersion,
        cap.Platform,
        cap.CapabilitiesJson,
        cap.UpdatedAt AS CapabilitiesUpdatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    LEFT JOIN dbo.ClientCapabilities cap ON cap.ClientId = c.Id
    WHERE c.RefId = @RefId;
END
GO

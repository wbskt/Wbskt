CREATE PROCEDURE dbo.Client_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.Id,
        c.RefId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.WorkspaceId,
        c.Name,
        c.Status,
        c.IsConnected,
        c.ConnectedAt,
        c.UpdatingUntil,
        c.LastActivityAt,
        -- A client whose presence has not changed since the column was added was last seen at its last change.
        ISNULL(c.LastSeenAt, c.LastActivityAt) AS LastSeenAt,
        c.LastRttMs,
        c.RttMeasuredAt,
        c.CreatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    WHERE c.RefId = @RefId;
END
GO

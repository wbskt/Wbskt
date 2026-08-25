CREATE PROCEDURE dbo.Client_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        C.Id,
        C.RefId,
        C.WorkspaceId,
        C.PolicyId,
        P.RefId AS PolicyRefId,
        C.Name,
        c.Status,
        c.IsConnected,
        c.ConnectedAt,
        c.LastActivityAt,
        c.LastRttMs,
        c.RttMeasuredAt,
        c.CreatedAt
    FROM dbo.Clients C
    JOIN dbo.RegistrationPolicies P ON C.PolicyId = P.Id
    WHERE C.Id = @Id;
END
GO

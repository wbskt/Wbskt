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
        C.Secret,
        c.Status,
        c.IsConnected,
        c.LastActivityAt,
        c.CreatedAt
    FROM dbo.Clients C
    JOIN dbo.RegistrationPolicies P ON C.PolicyId = P.Id
    WHERE C.Id = @Id;
END
GO

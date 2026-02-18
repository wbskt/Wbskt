CREATE PROCEDURE dbo.Client_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        C.Id,
        C.RefId,
        P.WorkspaceId,
        C.PolicyId,
        P.RefId AS PolicyRefId,
        C.Name,
        C.Secret,
        C.Status,
        C.CreatedAt
    FROM dbo.Clients C
    JOIN dbo.RegistrationPolicies P ON C.PolicyId = P.Id
    WHERE C.Id = @Id;
END
GO

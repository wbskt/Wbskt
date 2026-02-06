CREATE PROCEDURE dbo.Client_GetBy_PolicyId
    @PolicyId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.Id,
        c.RefId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.Name,
        '********' AS Secret,
        c.Status,
        c.CreatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    WHERE c.PolicyId = @PolicyId
    ORDER BY c.CreatedAt DESC;
END

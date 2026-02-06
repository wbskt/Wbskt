CREATE PROCEDURE dbo.Client_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        c.Id,
        c.RefId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.Name,
        c.Status,
        c.CreatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    ORDER BY c.CreatedAt DESC;
END

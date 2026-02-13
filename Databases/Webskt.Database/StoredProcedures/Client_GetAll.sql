CREATE PROCEDURE dbo.Client_GetAll
    @Status TINYINT = NULL,
    @Name NVARCHAR(100) = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Clients
    WHERE (@Status IS NULL OR Status = @Status)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%');

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
    WHERE (@Status IS NULL OR c.Status = @Status)
      AND (@Name IS NULL OR c.Name LIKE '%' + @Name + '%')
    ORDER BY c.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

CREATE PROCEDURE dbo.RegistrationPolicy_GetAll
    @AutoApproval BIT = NULL,
    @Name NVARCHAR(100) = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.RegistrationPolicies
    WHERE (@AutoApproval IS NULL OR AutoApproval = @AutoApproval)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%');

    SELECT 
        Id,
        RefId,
        Pin,
        Name,
        MaxClients,
        AutoApproval,
        CreatedAt
    FROM dbo.RegistrationPolicies
    WHERE (@AutoApproval IS NULL OR AutoApproval = @AutoApproval)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%')
    ORDER BY CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

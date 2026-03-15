CREATE PROCEDURE dbo.RegistrationPolicy_GetAll
    @WorkspaceId INT,
    @AutoApproval BIT = NULL,
    @Name NVARCHAR(100) = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Total count for the workspace
    SELECT @TotalCount = COUNT(*)
    FROM dbo.RegistrationPolicies
    WHERE WorkspaceId = @WorkspaceId
      AND (@AutoApproval IS NULL OR AutoApproval = @AutoApproval)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%');

    -- Paginated selection
    SELECT 
        Id,
        RefId,
        WorkspaceId,
        Pin,
        Name,
        MaxClients,
        AutoApproval,
        IsEnabled,
        CreatedAt    FROM dbo.RegistrationPolicies
    WHERE WorkspaceId = @WorkspaceId
      AND  (@AutoApproval IS NULL OR AutoApproval = @AutoApproval)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%')
    ORDER BY CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

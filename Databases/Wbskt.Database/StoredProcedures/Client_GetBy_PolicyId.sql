CREATE PROCEDURE dbo.Client_GetBy_PolicyId
    @WorkspaceId INT,
    @PolicyId INT,
    @Status TINYINT = NULL,
    @Name NVARCHAR(100) = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @Tag NVARCHAR(32) = NULL,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Total count for this policy
    SELECT @TotalCount = COUNT(*)
    FROM dbo.Clients
    WHERE WorkspaceId = @WorkspaceId
      AND  PolicyId = @PolicyId
      AND (@Status IS NULL OR Status = @Status)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%')
      AND (@Tag IS NULL OR EXISTS (SELECT 1 FROM dbo.ClientTags t WHERE t.ClientId = Clients.Id AND t.Tag = @Tag));

    -- Filtered and paginated selection
    SELECT 
        c.Id,
        c.RefId,
        c.WorkspaceId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.Name,
        c.Status,
        c.IsConnected,
        c.ConnectedAt,
        c.LastActivityAt,
        c.LastRttMs,
        c.RttMeasuredAt,
        c.CreatedAt,
        (SELECT STRING_AGG(t.Tag, ',') WITHIN GROUP (ORDER BY t.Tag)
         FROM dbo.ClientTags t WHERE t.ClientId = c.Id) AS Tags
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    WHERE  c.WorkspaceId = @WorkspaceId
      AND  c.PolicyId = @PolicyId
      AND (@Status IS NULL OR c.Status = @Status)
      AND (@Name IS NULL OR c.Name LIKE '%' + @Name + '%')
      AND (@Tag IS NULL OR EXISTS (SELECT 1 FROM dbo.ClientTags t WHERE t.ClientId = c.Id AND t.Tag = @Tag))
    ORDER BY c.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

CREATE PROCEDURE dbo.Client_GetAll
    @WorkspaceId INT,
    @Name NVARCHAR(100) = NULL,
    @Status TINYINT = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @Tag NVARCHAR(32) = NULL,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Clients   
    WHERE WorkspaceId = @WorkspaceId
      AND (@Status IS NULL OR Status = @Status)
      AND (@Name IS NULL OR Name LIKE '%' + @Name + '%')
      AND (@Tag IS NULL OR EXISTS (SELECT 1 FROM dbo.ClientTags t WHERE t.ClientId = Clients.Id AND t.Tag = @Tag));

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
    WHERE c.WorkspaceId = @WorkspaceId
      AND (@Status IS NULL OR c.Status = @Status)
      AND (@Name IS NULL OR c.Name LIKE '%' + @Name + '%')
      AND (@Tag IS NULL OR EXISTS (SELECT 1 FROM dbo.ClientTags t WHERE t.ClientId = c.Id AND t.Tag = @Tag))
    ORDER BY c.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

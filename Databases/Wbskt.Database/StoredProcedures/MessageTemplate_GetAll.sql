CREATE PROCEDURE dbo.MessageTemplate_GetAll
    @WorkspaceId INT,
    @PolicyId INT = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.MessageTemplates
    WHERE WorkspaceId = @WorkspaceId
      AND (@PolicyId IS NULL OR PolicyId = @PolicyId);

    SELECT
        mt.Id,
        mt.RefId,
        mt.WorkspaceId,
        mt.PolicyId,
        p.RefId AS PolicyRefId,
        mt.Name,
        mt.MessageType,
        mt.PayloadJson,
        mt.CreatedAt,
        mt.UpdatedAt
    FROM dbo.MessageTemplates mt
    LEFT JOIN dbo.RegistrationPolicies p ON mt.PolicyId = p.Id
    WHERE mt.WorkspaceId = @WorkspaceId
      AND (@PolicyId IS NULL OR mt.PolicyId = @PolicyId)
    ORDER BY mt.Name
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO

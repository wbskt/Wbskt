CREATE PROCEDURE dbo.MessageTemplate_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

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
    WHERE mt.RefId = @RefId;
END
GO

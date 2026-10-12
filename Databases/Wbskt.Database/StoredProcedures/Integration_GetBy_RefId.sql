CREATE PROCEDURE dbo.Integration_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        I.Id, I.RefId, I.WorkspaceId, I.Type, I.Preset, I.Name, I.ConfigJson, I.SecretCiphertext, I.KeyId,
        I.SecretHint, I.Metadata, I.LastTestAt, I.LastTestOk, I.LastTestCode, I.LastTestError, I.LastUsedAt,
        I.CreatedBy, I.CreatedByRefId, I.CreatedAt, I.UpdatedAt, I.SecretRotatedAt,
        U.UsageWorkflows, U.UsageSteps
    FROM dbo.Integrations I
    OUTER APPLY (
        SELECT COUNT(DISTINCT IU.WorkflowRefId) AS UsageWorkflows, COUNT(*) AS UsageSteps
        FROM dbo.IntegrationUsages IU
        WHERE IU.IntegrationId = I.Id
    ) U
    WHERE I.RefId = @RefId;
END
GO

-- A workspace's integrations, optionally of one type, by name. Unpaged: a workspace holds at most
-- a few dozen. Each row carries how many live workflows, and how many of their steps, use it.
CREATE PROCEDURE dbo.Integration_GetAll_By_WorkspaceId
    @WorkspaceId INT,
    @Type        TINYINT = NULL
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
    WHERE I.WorkspaceId = @WorkspaceId
      AND (@Type IS NULL OR I.Type = @Type)
    ORDER BY I.Name;
END
GO

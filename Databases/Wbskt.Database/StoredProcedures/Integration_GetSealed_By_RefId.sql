-- What the engine needs to send through an integration: no usage, no audit columns.
CREATE PROCEDURE dbo.Integration_GetSealed_By_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, RefId, WorkspaceId, Type, Preset, ConfigJson, SecretCiphertext, KeyId, LastUsedAt
    FROM dbo.Integrations
    WHERE RefId = @RefId;
END
GO

-- Renames an integration, or replaces its settings and resealed secret. @SecretCiphertext NULL
-- keeps the stored one. A change to the settings or the secret makes the last test describe
-- something that is no longer saved, so it is cleared; a new secret also clears what that test
-- learned and stamps SecretRotatedAt.
CREATE PROCEDURE dbo.Integration_Update
    @Id               INT,
    @WorkspaceId      INT,
    @Name             NVARCHAR(100),
    @ConfigJson       NVARCHAR(MAX),
    @ConfigChanged    BIT,
    @SecretCiphertext VARBINARY(MAX) = NULL,
    @KeyId            TINYINT = NULL,
    @SecretHint       NVARCHAR(100) = NULL,
    @SecretRotated    BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Integrations
    SET Name             = @Name,
        ConfigJson       = @ConfigJson,
        SecretCiphertext = ISNULL(@SecretCiphertext, SecretCiphertext),
        KeyId            = ISNULL(@KeyId, KeyId),
        SecretHint       = CASE WHEN @SecretCiphertext IS NULL THEN SecretHint ELSE @SecretHint END,
        SecretRotatedAt  = CASE WHEN @SecretRotated = 1 THEN SYSUTCDATETIME() ELSE SecretRotatedAt END,
        Metadata         = CASE WHEN @SecretRotated = 1 THEN NULL ELSE Metadata END,
        LastTestAt       = CASE WHEN @ConfigChanged = 1 OR @SecretRotated = 1 THEN NULL ELSE LastTestAt END,
        LastTestOk       = CASE WHEN @ConfigChanged = 1 OR @SecretRotated = 1 THEN NULL ELSE LastTestOk END,
        LastTestCode     = CASE WHEN @ConfigChanged = 1 OR @SecretRotated = 1 THEN NULL ELSE LastTestCode END,
        LastTestError    = CASE WHEN @ConfigChanged = 1 OR @SecretRotated = 1 THEN NULL ELSE LastTestError END,
        UpdatedAt        = SYSUTCDATETIME()
    WHERE Id = @Id
      AND WorkspaceId = @WorkspaceId;
END
GO

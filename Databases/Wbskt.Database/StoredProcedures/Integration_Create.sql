-- Adds an integration whose secret the engine has already sealed (under @RefId, which the caller
-- chose, because the seal is bound to it). Refuses with 50031 when the workspace already holds
-- @MaxPerWorkspace; a duplicate name fails on UQ_Integrations_WorkspaceId_Name (2627).
CREATE PROCEDURE dbo.Integration_Create
    @RefId            UNIQUEIDENTIFIER,
    @WorkspaceId      INT,
    @Type             TINYINT,
    @Preset           TINYINT = NULL,
    @Name             NVARCHAR(100),
    @ConfigJson       NVARCHAR(MAX),
    @SecretCiphertext VARBINARY(MAX),
    @KeyId            TINYINT,
    @SecretHint       NVARCHAR(100) = NULL,
    @CreatedBy        INT,
    @CreatedByRefId   UNIQUEIDENTIFIER = NULL,
    @MaxPerWorkspace  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    -- Under the range lock two concurrent adds cannot both see room for one more.
    IF (SELECT COUNT(*) FROM dbo.Integrations WITH (UPDLOCK, HOLDLOCK) WHERE WorkspaceId = @WorkspaceId) >= @MaxPerWorkspace
    BEGIN
        ROLLBACK TRAN;
        THROW 50031, 'The workspace has reached its integration limit.', 1;
    END

    INSERT INTO dbo.Integrations (RefId, WorkspaceId, Type, Preset, Name, ConfigJson, SecretCiphertext, KeyId, SecretHint, CreatedBy, CreatedByRefId, SecretRotatedAt)
    VALUES (@RefId, @WorkspaceId, @Type, @Preset, @Name, @ConfigJson, @SecretCiphertext, @KeyId, @SecretHint, @CreatedBy, @CreatedByRefId, NULL);

    COMMIT TRAN;
END
GO

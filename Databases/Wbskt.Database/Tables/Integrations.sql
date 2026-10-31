-- A workspace's accounts with outside services (a Telegram bot, an SMTP relay, a Home Assistant
-- token) that workflow steps send through. The credential is sealed by the workflow engine with
-- AES-256-GCM under the master key KeyId names; this database never sees it in the clear, and the
-- management host never holds the key. The associated data binds the ciphertext to WorkspaceId,
-- RefId and Type, so a sealed secret copied onto another row does not open.
CREATE TABLE dbo.Integrations (
    Id               INT              IDENTITY(1, 1) NOT NULL,
    RefId            UNIQUEIDENTIFIER NOT NULL,
    WorkspaceId      INT              NOT NULL,
    Type             TINYINT          NOT NULL, -- IntegrationType: 1 Telegram, 2 Discord, 3 Smtp, 4 Ntfy, 5 HttpAuth (append-only)
    Preset           TINYINT          NULL,     -- IntegrationPreset: 1 HomeAssistant (an HttpAuth shape)
    Name             NVARCHAR(100)    NOT NULL,
    ConfigJson       NVARCHAR(MAX)    NOT NULL, -- the non-secret settings, as the API shows them
    SecretCiphertext VARBINARY(MAX)   NOT NULL, -- nonce | tag | ciphertext
    KeyId            TINYINT          NOT NULL, -- which master key sealed it
    SecretHint       NVARCHAR(100)    NULL,     -- e.g. "••••Xk2Q", so a reader can tell which token is saved
    Metadata         NVARCHAR(MAX)    NULL,     -- what the last successful test learned, e.g. the bot's username
    LastTestAt       DATETIME2(3)     NULL,
    LastTestOk       BIT              NULL,
    LastTestCode     NVARCHAR(100)    NULL,
    LastTestError    NVARCHAR(400)    NULL,
    LastUsedAt       DATETIME2(3)     NULL,     -- written by the engine, at most once a minute
    CreatedBy        INT              NOT NULL,
    CreatedByRefId   UNIQUEIDENTIFIER NULL,
    CreatedAt        DATETIME2(3)     NOT NULL CONSTRAINT DF_Integrations_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt        DATETIME2(3)     NOT NULL CONSTRAINT DF_Integrations_UpdatedAt DEFAULT SYSUTCDATETIME(),
    SecretRotatedAt  DATETIME2(3)     NULL,

    CONSTRAINT PK_Integrations                  PRIMARY KEY (Id),
    CONSTRAINT UQ_Integrations_RefId            UNIQUE (RefId),
    CONSTRAINT UQ_Integrations_WorkspaceId_Name UNIQUE (WorkspaceId, Name),
    CONSTRAINT CK_Integrations_Type             CHECK (Type >= 1 AND Type <= 5),
    CONSTRAINT CK_Integrations_Preset           CHECK (Preset IS NULL OR (Preset = 1 AND Type = 5))
);
GO

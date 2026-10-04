CREATE TABLE dbo.RefreshTokens
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId INT NOT NULL,

    -- SHA-256 of the token as the client holds it (SecurityTokens.Hash). The token itself is a bearer
    -- credential for a week of sessions, so a read of this table, a backup or a log of a query must
    -- not be enough to sign in as anyone.
    TokenHash VARBINARY(32) NOT NULL,

    -- When the session (or this rotation of it) was issued. Shown in the session list.
    Created DATETIME2(3) NOT NULL CONSTRAINT DF_RefreshTokens_Created DEFAULT SYSUTCDATETIME(),
    Expires DATETIME2(3) NOT NULL,
    Revoked DATETIME2(3) NULL,
    CreatedByIp NVARCHAR(50) NULL,
    RevokedByIp NVARCHAR(50) NULL,

    -- Hash of the token this one was rotated into, for tracing a session family. Never the token.
    ReplacedByTokenHash VARBINARY(32) NULL,

    -- One sign-in, carried unchanged across every rotation. It is the session id the API and the
    -- access token's sid claim use, so it stays valid while the token underneath it changes.
    SessionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_RefreshTokens_SessionId DEFAULT NEWID(),

    -- When that sign-in happened, also carried across rotations. dbo.RefreshToken_Rotate refuses to
    -- extend a session past an absolute lifetime counted from here, so a refresh token that keeps
    -- being rotated (by its owner or by a thief) still ends in a fresh password entry.
    SessionStarted DATETIME2(3) NOT NULL CONSTRAINT DF_RefreshTokens_SessionStarted DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_RefreshTokens_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),

    -- Every lookup is by token hash, and the service assumes a token identifies at most one row.
    CONSTRAINT UQ_RefreshTokens_TokenHash UNIQUE (TokenHash)
)
GO

-- Supports revoking a user's whole token set (sign-out-everywhere, deactivation, leak response).
CREATE NONCLUSTERED INDEX IX_RefreshTokens_UserId
    ON dbo.RefreshTokens (UserId)
    INCLUDE (Revoked)
GO

-- Ending one session (dbo.RefreshToken_RevokeForUser) finds its live row by session id.
CREATE NONCLUSTERED INDEX IX_RefreshTokens_SessionId
    ON dbo.RefreshTokens (SessionId)
    INCLUDE (UserId, Revoked)
GO

-- Drives the retention sweep (dbo.Credential_DeleteExpired).
CREATE NONCLUSTERED INDEX IX_RefreshTokens_Expires
    ON dbo.RefreshTokens (Expires)
GO

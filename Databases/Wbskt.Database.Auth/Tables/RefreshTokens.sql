CREATE TABLE dbo.RefreshTokens
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId INT NOT NULL,
    Token NVARCHAR(255) NOT NULL,
    Expires DATETIME2(3) NOT NULL,
    Revoked DATETIME2(3) NULL,
    CreatedByIp NVARCHAR(50) NULL,
    RevokedByIp NVARCHAR(50) NULL,
    ReplacedByToken NVARCHAR(255) NULL,
    CONSTRAINT FK_RefreshTokens_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),

    -- Every lookup is by token value, and the service assumes a token identifies at most one row.
    CONSTRAINT UQ_RefreshTokens_Token UNIQUE (Token)
)
GO

-- Supports revoking a user's whole token set (sign-out-everywhere, deactivation, leak response).
CREATE NONCLUSTERED INDEX IX_RefreshTokens_UserId
    ON dbo.RefreshTokens (UserId)
    INCLUDE (Revoked)
GO

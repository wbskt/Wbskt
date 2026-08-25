-- A pending proof that someone controls the address on their account. Same shape and the same
-- reasoning as PasswordResetTokens: hash only, single-use, short-lived.
--
-- Unlike a password reset, an account can sit unverified indefinitely -- it simply cannot sign in --
-- so resend-verification issues a fresh token and supersedes the old one.
CREATE TABLE dbo.EmailVerificationTokens (
    Id         INT              IDENTITY(1, 1) NOT NULL,
    UserId     INT              NOT NULL,
    TokenHash  VARBINARY(32)    NOT NULL,
    ExpiresAt  DATETIME2(3)     NOT NULL,
    ConsumedAt DATETIME2(3)     NULL,
    CreatedAt  DATETIME2(3)     NOT NULL CONSTRAINT DF_EmailVerificationTokens_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_EmailVerificationTokens PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_EmailVerificationTokens_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_EmailVerificationTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
GO

CREATE UNIQUE INDEX UX_EmailVerificationTokens_Outstanding
    ON dbo.EmailVerificationTokens (UserId)
    WHERE ConsumedAt IS NULL;
GO

CREATE INDEX IX_EmailVerificationTokens_ExpiresAt
    ON dbo.EmailVerificationTokens (ExpiresAt);
GO

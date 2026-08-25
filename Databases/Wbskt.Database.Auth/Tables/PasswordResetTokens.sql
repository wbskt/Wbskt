-- A pending offer to set a new password without knowing the old one. Modelled on TenantInvitations,
-- for the same reason: this is a bearer credential, so the row stores the hash of the token and never
-- the token, and a read of this table hands out nothing.
--
-- Single-use and short-lived. PasswordResetToken_Consume is the only procedure that may act on one,
-- and it stamps ConsumedAt in the same transaction that writes the password.
CREATE TABLE dbo.PasswordResetTokens (
    Id            INT              IDENTITY(1, 1) NOT NULL,
    UserId        INT              NOT NULL,
    TokenHash     VARBINARY(32)    NOT NULL,
    ExpiresAt     DATETIME2(3)     NOT NULL,
    ConsumedAt    DATETIME2(3)     NULL,

    -- Who asked. Retained because a burst of resets for one account from one address is the shape of
    -- an attack, and there is no way to see it after the fact otherwise.
    RequestedByIp NVARCHAR(50)     NULL,

    CreatedAt     DATETIME2(3)     NOT NULL CONSTRAINT DF_PasswordResetTokens_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_PasswordResetTokens PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_PasswordResetTokens_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_PasswordResetTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
GO

-- One outstanding token per account. Asking again is the normal way to replace a link that was lost
-- or has expired, so PasswordResetToken_Create consumes any predecessor rather than failing here --
-- the index exists to make that invariant enforced rather than merely intended.
CREATE UNIQUE INDEX UX_PasswordResetTokens_Outstanding
    ON dbo.PasswordResetTokens (UserId)
    WHERE ConsumedAt IS NULL;
GO

-- Supports the retention sweep. Spent and expired rows are kept as history, not forever.
CREATE INDEX IX_PasswordResetTokens_ExpiresAt
    ON dbo.PasswordResetTokens (ExpiresAt);
GO

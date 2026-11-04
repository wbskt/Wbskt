-- A pending change of an account's address, and the undo for one that went through. Same shape and
-- the same reasoning as PasswordResetTokens: hash only, single-use, short-lived.
--
-- Kind 1 (confirm) is mailed to the new address; following it makes the switch. Kind 2 (revert) is
-- mailed to the old address once the switch is made and puts it back for 7 days, which is the way
-- out when a stolen session pointed the account at someone else's mailbox.
CREATE TABLE dbo.EmailChangeTokens (
    Id            INT              IDENTITY(1, 1) NOT NULL,
    UserId        INT              NOT NULL,
    Kind          TINYINT          NOT NULL,
    TokenHash     VARBINARY(32)    NOT NULL,
    OldEmail      NVARCHAR(100)    NOT NULL,
    NewEmail      NVARCHAR(100)    NOT NULL,
    ExpiresAt     DATETIME2(3)     NOT NULL,
    ConsumedAt    DATETIME2(3)     NULL,
    RequestedByIp NVARCHAR(50)     NULL,
    CreatedAt     DATETIME2(3)     NOT NULL CONSTRAINT DF_EmailChangeTokens_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_EmailChangeTokens PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_EmailChangeTokens_TokenHash UNIQUE (TokenHash),
    CONSTRAINT CK_EmailChangeTokens_Kind CHECK (Kind = 1 OR Kind = 2),
    CONSTRAINT FK_EmailChangeTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
GO

-- At most one pending change per account; a new request replaces the old one. Undos are not limited:
-- each change leaves its own, so a second change cannot take away the owner's way back from the first.
CREATE UNIQUE INDEX UX_EmailChangeTokens_Outstanding
    ON dbo.EmailChangeTokens (UserId)
    WHERE ConsumedAt IS NULL AND Kind = 1;
GO

CREATE INDEX IX_EmailChangeTokens_ExpiresAt
    ON dbo.EmailChangeTokens (ExpiresAt);
GO

-- Sign-up and confirm look up whether a live undo holds an address.
CREATE INDEX IX_EmailChangeTokens_LiveUndo
    ON dbo.EmailChangeTokens (OldEmail)
    INCLUDE (UserId, ExpiresAt)
    WHERE ConsumedAt IS NULL AND Kind = 2;
GO

-- Browsers that have signed in to an account with its password. Each holds a random token in an
-- HttpOnly cookie (the sign-in device cookie); this table keeps only its SHA-256. While the account is
-- locked after too many wrong passwords, a browser with a live token for it may still try the password
-- (dbo.UserDevice_Verify), so a stranger who locks the account on purpose does not lock its owner out
-- of their own browser (#942). The token is no credential on its own: the password is still checked.
CREATE TABLE dbo.UserDevices
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId INT NOT NULL,
    TokenHash VARBINARY(32) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserDevices_CreatedAt DEFAULT SYSUTCDATETIME(),
    ExpiresAt DATETIME2(3) NOT NULL,

    CONSTRAINT FK_UserDevices_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT UQ_UserDevices_TokenHash UNIQUE (TokenHash)
);
GO

CREATE INDEX IX_UserDevices_UserId ON dbo.UserDevices (UserId, ExpiresAt);
GO

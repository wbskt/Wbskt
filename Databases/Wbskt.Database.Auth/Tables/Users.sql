CREATE TABLE dbo.Users (
    Id           INT              IDENTITY(1, 1) NOT NULL,
    RefId        UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    Username     NVARCHAR(50)     NOT NULL,
    Email        NVARCHAR(100)    NOT NULL,
    PasswordHash NVARCHAR(255)    NOT NULL,
    IsActive     BIT              NOT NULL           DEFAULT 1,

    -- Whether the owner of this address has proved they control it. Sign-in requires it, so the
    -- default is 0 and only EmailVerificationToken_Consume ever sets it. Accounts that predate this
    -- column are backfilled to 1 by the pre-deployment script -- they were created when registering
    -- proved nothing, and locking them out retroactively would be a data migration that removes
    -- access rather than adding a check.
    IsEmailVerified BIT           NOT NULL           CONSTRAINT DF_Users_IsEmailVerified DEFAULT 0,

    CreatedAt    DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Per-account sign-in lockout. Wrong passwords count up to the service's limit, which locks the
    -- account until LockedUntil and starts the count again. A successful sign-in, a password change
    -- or a reset clears both. The per-IP rate limiter cannot do this: a guesser spread across many
    -- addresses never trips it.
    FailedLoginCount  INT          NOT NULL           CONSTRAINT DF_Users_FailedLoginCount DEFAULT 0,
    LockedUntil       DATETIME2(3) NULL,

    LastLoginAt       DATETIME2(3) NULL,
    PasswordChangedAt DATETIME2(3) NULL,

    -- Constraints
    CONSTRAINT PK_Users          PRIMARY KEY (Id),
    CONSTRAINT UQ_Users_RefId    UNIQUE (RefId),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT UQ_Users_Email    UNIQUE (Email)
);
GO

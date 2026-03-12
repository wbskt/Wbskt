CREATE TABLE dbo.Users (
    Id           INT              IDENTITY(1, 1) NOT NULL,
    RefId        UNIQUEIDENTIFIER NOT NULL           DEFAULT NEWID(),
    Username     NVARCHAR(50)     NOT NULL,
    Email        NVARCHAR(100)    NOT NULL,
    PasswordHash NVARCHAR(255)    NOT NULL,
    IsActive     BIT              NOT NULL           DEFAULT 1,
    CreatedAt    DATETIME2(3)     NOT NULL           DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_Users          PRIMARY KEY (Id),
    CONSTRAINT UQ_Users_RefId    UNIQUE (RefId),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT UQ_Users_Email    UNIQUE (Email)
);
GO

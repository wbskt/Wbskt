CREATE TABLE dbo.Permissions (
    Id          INT           IDENTITY(1, 1) NOT NULL,
    Slug        NVARCHAR(100) NOT NULL,
    Description NVARCHAR(255) NULL,

    -- Constraints
    CONSTRAINT PK_Permissions      PRIMARY KEY (Id),
    CONSTRAINT UQ_Permissions_Slug UNIQUE (Slug)
);
GO

-- One-time data changes the post-deployment script has applied, by name. A change that must happen
-- exactly once (granting a new default to roles that already exist, say) checks for its name and
-- records it in the same batch, so a later deploy does not overrule an administrator who undid it.
CREATE TABLE dbo.DataMigrations
(
    Name NVARCHAR(100) NOT NULL CONSTRAINT PK_DataMigrations PRIMARY KEY,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DataMigrations_CreatedAt DEFAULT SYSUTCDATETIME()
);
GO

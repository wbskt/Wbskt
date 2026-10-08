-- A set of device tags, already normalised by the caller.
CREATE TYPE dbo.TagTableType AS TABLE
(
    Tag NVARCHAR(32) NOT NULL PRIMARY KEY
);
GO

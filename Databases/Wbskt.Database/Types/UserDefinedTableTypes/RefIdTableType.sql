-- A set of references, for a procedure that matches against it.
CREATE TYPE dbo.RefIdTableType AS TABLE
(
    RefId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
);
GO

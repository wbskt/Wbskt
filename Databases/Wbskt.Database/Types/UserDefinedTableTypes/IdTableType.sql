-- A set of row ids, for a procedure that reads details for a page of rows it was handed.
CREATE TYPE dbo.IdTableType AS TABLE
(
    Id INT NOT NULL PRIMARY KEY
);
GO

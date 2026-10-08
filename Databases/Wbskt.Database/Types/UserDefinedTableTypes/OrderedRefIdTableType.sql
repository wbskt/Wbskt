-- A list of references whose order matters to the procedure reading it (Ordinal ascending).
CREATE TYPE dbo.OrderedRefIdTableType AS TABLE
(
    Ordinal INT              NOT NULL PRIMARY KEY,
    RefId   UNIQUEIDENTIFIER NOT NULL
);
GO

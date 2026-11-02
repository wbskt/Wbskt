-- One state report's variables, distinct by name; Ordinal is the order they were reported in.
CREATE TYPE dbo.ClientStateVariableTableType AS TABLE
(
    Ordinal   INT           NOT NULL PRIMARY KEY,
    Name      NVARCHAR(100) NOT NULL UNIQUE,
    DataType  NVARCHAR(20)  NOT NULL,
    ValueJson NVARCHAR(MAX) NOT NULL
);
GO

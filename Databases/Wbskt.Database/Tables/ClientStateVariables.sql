-- Last-known state variables self-reported by clients via "state.report" messages.
-- Change history is not kept here; every change is emitted as ClientPropertyUpdatedEvent
-- and lands in dbo.EventLogs.
CREATE TABLE dbo.ClientStateVariables (
    Id        INT           IDENTITY(1, 1) NOT NULL,
    ClientId  INT           NOT NULL,
    Name      NVARCHAR(100) NOT NULL,
    DataType  NVARCHAR(20)  NOT NULL, -- JSON value kind: number | string | boolean | object | array | null
    ValueJson NVARCHAR(MAX) NOT NULL,
    UpdatedAt DATETIME2(3)  NOT NULL  DEFAULT SYSUTCDATETIME(),

    -- Constraints
    CONSTRAINT PK_ClientStateVariables             PRIMARY KEY (Id),
    CONSTRAINT UQ_ClientStateVariables_Client_Name UNIQUE (ClientId, Name),
    CONSTRAINT FK_ClientStateVariables_Clients     FOREIGN KEY (ClientId)
        REFERENCES dbo.Clients (Id)
);
GO

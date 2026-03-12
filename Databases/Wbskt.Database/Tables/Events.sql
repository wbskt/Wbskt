CREATE TABLE dbo.Events (
    Id               INT           IDENTITY(1, 1) NOT NULL,
    EventName        NVARCHAR(100) NOT NULL,
    EventCriticality TINYINT       NOT NULL,

    -- Constraints
    CONSTRAINT PK_Events          PRIMARY KEY (Id),
    CONSTRAINT UQ_Events_EventName UNIQUE (EventName)
);
GO
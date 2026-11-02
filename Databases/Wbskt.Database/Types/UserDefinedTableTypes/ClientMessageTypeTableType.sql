CREATE TYPE dbo.ClientMessageTypeTableType AS TABLE
(
    ClientId      INT            NOT NULL,
    Type          NVARCHAR(100)  NOT NULL,
    MessageCount  INT            NOT NULL,
    LastSeenAt    DATETIME2(3)   NOT NULL,
    SamplePayload NVARCHAR(4000) NULL,
    PRIMARY KEY (ClientId, Type)
);
GO

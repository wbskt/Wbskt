-- The steps of a definition being published that name an integration, by the integration's RefId.
CREATE TYPE dbo.IntegrationRefTableType AS TABLE
(
    NodeId           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    NodeName         NVARCHAR(200)    NOT NULL,
    IntegrationRefId UNIQUEIDENTIFIER NOT NULL
);
GO

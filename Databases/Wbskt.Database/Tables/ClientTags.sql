-- Free-form labels on a device ("garage", "greenhouse") for grouping and filtering the client list.
-- Tags are stored as the management host normalises them: trimmed, lower-case, 1-32 characters of
-- letters, digits, spaces, '-', '_' and '.', so they never contain the ',' list reads join them with.
CREATE TABLE dbo.ClientTags (
    ClientId INT          NOT NULL,
    Tag      NVARCHAR(32) NOT NULL,

    -- Constraints
    CONSTRAINT PK_ClientTags         PRIMARY KEY (ClientId, Tag),
    CONSTRAINT FK_ClientTags_Clients FOREIGN KEY (ClientId)
        REFERENCES dbo.Clients (Id)
);
GO

-- The client list's tag filter looks clients up by tag.
CREATE INDEX IX_ClientTags_Tag
    ON dbo.ClientTags (Tag, ClientId);
GO

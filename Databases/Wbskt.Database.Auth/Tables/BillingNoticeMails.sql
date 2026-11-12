-- Billing emails waiting to be handed to the relay, written in the same transaction as their notice's
-- claim (dbo.BillingNotice_Record), so a notice claimed is a notice that will be sent: a restart or a
-- relay that refuses only delays it. dbo.BillingNoticeMails_Take leases due rows to one sender at a time;
-- SentAt marks one the relay accepted, and GaveUpAt one that kept failing. Sent and given-up rows are
-- removed after a while (dbo.BillingNoticeMails_Purge); the claim in dbo.BillingNotices stays.
CREATE TABLE dbo.BillingNoticeMails
(
    Id            BIGINT         IDENTITY(1, 1) NOT NULL,
    TenantId      INT            NOT NULL,
    Kind          NVARCHAR(40)   NOT NULL,
    PeriodKey     NVARCHAR(200)  NOT NULL,
    ToAddress     NVARCHAR(100)  NOT NULL,
    Subject       NVARCHAR(400)  NOT NULL,
    HtmlBody      NVARCHAR(MAX)  NOT NULL,
    Attempts      INT            NOT NULL CONSTRAINT DF_BillingNoticeMails_Attempts DEFAULT 0,
    NextAttemptAt DATETIME2(3)   NOT NULL CONSTRAINT DF_BillingNoticeMails_NextAttemptAt DEFAULT SYSUTCDATETIME(),
    LastError     NVARCHAR(1000) NULL,
    SentAt        DATETIME2(3)   NULL,
    GaveUpAt      DATETIME2(3)   NULL,
    CreatedAt     DATETIME2(3)   NOT NULL CONSTRAINT DF_BillingNoticeMails_CreatedAt DEFAULT SYSUTCDATETIME(),
    -- Last, so adding it was an ALTER rather than a table rebuild. Null on rows queued before it existed.
    TextBody      NVARCHAR(MAX)  NULL,
    CONSTRAINT PK_BillingNoticeMails PRIMARY KEY (Id),
    CONSTRAINT FK_BillingNoticeMails_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id) ON DELETE CASCADE
);
GO

CREATE INDEX IX_BillingNoticeMails_Due ON dbo.BillingNoticeMails (NextAttemptAt) WHERE SentAt IS NULL AND GaveUpAt IS NULL;
GO

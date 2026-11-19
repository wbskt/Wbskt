-- What platform staff did through the admin host (wbskt/Wbskt.Admin), one row per attempt. Written by
-- the host that owns the change, in the same transaction as the change, so a change is never without
-- its row. Append-only: TR_StaffAudit_AppendOnly refuses every update, and every delete of a row younger
-- than seven years (StaffAudit_DeleteExpired, StaffAuditRetention), including from a procedure, which
-- ownership chaining would otherwise let past a DENY. Rows hold references, not names or addresses, so
-- they say nothing about a person once that person's account is gone.
CREATE TABLE dbo.StaffAudit
(
    Id             BIGINT           IDENTITY (1, 1) NOT NULL,
    StaffRefId     UNIQUEIDENTIFIER NOT NULL,
    StaffName      NVARCHAR(100)    NOT NULL,
    Action         NVARCHAR(64)     NOT NULL,
    TargetKind     NVARCHAR(32)     NOT NULL,
    TargetRefId    UNIQUEIDENTIFIER NULL,
    Reason         NVARCHAR(500)    NOT NULL,
    Outcome        NVARCHAR(16)     NOT NULL,
    Detail         NVARCHAR(MAX)    NULL,
    IdempotencyKey UNIQUEIDENTIFIER NOT NULL,
    CreatedAt      DATETIME2(3)     NOT NULL CONSTRAINT DF_StaffAudit_CreatedAt DEFAULT SYSUTCDATETIME(),
    -- What within the target the change was about: the member of an account, or one of its credit grants.
    SubjectRefId   UNIQUEIDENTIFIER NULL,
    -- SHA-256 of the request without its idempotency key (StaffRequestHash), so a key sent again with a
    -- different body is refused rather than answered with this change. Null on rows written before it.
    BodyHash       VARBINARY(32)    NULL,
    CONSTRAINT PK_StaffAudit PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_StaffAudit_IdempotencyKey UNIQUE (IdempotencyKey),
    CONSTRAINT CK_StaffAudit_Outcome CHECK (Outcome = N'Succeeded' OR Outcome = N'Refused'),
    CONSTRAINT CK_StaffAudit_Reason CHECK (LEN(Reason) > 0)
);
GO

CREATE NONCLUSTERED INDEX IX_StaffAudit_TargetRefId
    ON dbo.StaffAudit (TargetRefId, Id DESC)
    WHERE TargetRefId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_StaffAudit_SubjectRefId
    ON dbo.StaffAudit (SubjectRefId, Id DESC)
    WHERE SubjectRefId IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX IX_StaffAudit_CreatedAt
    ON dbo.StaffAudit (CreatedAt);
GO

CREATE NONCLUSTERED INDEX IX_StaffAudit_StaffRefId
    ON dbo.StaffAudit (StaffRefId, Id DESC);
GO

CREATE TRIGGER dbo.TR_StaffAudit_AppendOnly
ON dbo.StaffAudit
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- An update is never allowed. A delete is, of rows past the retention only; the delete below does
    -- not fire this trigger again.
    IF EXISTS (SELECT 1 FROM inserted)
       OR EXISTS (SELECT 1 FROM deleted WHERE CreatedAt >= DATEADD(YEAR, -7, SYSUTCDATETIME()))
        THROW 50090, N'dbo.StaffAudit is append-only.', 1;

    DELETE SA
    FROM dbo.StaffAudit SA
    JOIN deleted D ON D.Id = SA.Id;
END
GO

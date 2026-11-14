-- One staff audit row, written by the staff action runner (Wbskt.Infrastructure.StaffActions) in the
-- transaction of the change it records. A second row for the same idempotency key fails on
-- UQ_StaffAudit_IdempotencyKey, which is what makes a retried call run once.
CREATE PROCEDURE dbo.StaffAudit_Insert
    @StaffRefId     UNIQUEIDENTIFIER,
    @StaffName      NVARCHAR(100),
    @Action         NVARCHAR(64),
    @TargetKind     NVARCHAR(32),
    @TargetRefId    UNIQUEIDENTIFIER,
    @Reason         NVARCHAR(500),
    @Outcome        NVARCHAR(16),
    @Detail         NVARCHAR(MAX),
    @IdempotencyKey UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.StaffAudit
    (
        StaffRefId,
        StaffName,
        Action,
        TargetKind,
        TargetRefId,
        Reason,
        Outcome,
        Detail,
        IdempotencyKey
    )
    VALUES
    (
        @StaffRefId,
        @StaffName,
        @Action,
        @TargetKind,
        @TargetRefId,
        @Reason,
        @Outcome,
        @Detail,
        @IdempotencyKey
    );

    SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS Id;
END

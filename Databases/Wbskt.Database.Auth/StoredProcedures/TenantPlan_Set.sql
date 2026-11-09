-- Puts a tenant on a plan, with optional negotiated limits, recording who set it (@PlanSource) and, for
-- an operator's plan, when it ends (@PlanEndsAt), if its plan is still at
-- @ExpectedRevision: a change worked out from a stale read is refused (no row returned) rather than
-- silently replacing a newer one. On success the revision goes up by one and TenantPlanChangedEvent is
-- written to dbo.OutboxMessages in the same transaction, so a plan change that commits is always
-- announced to the main database's copies, even if the host stops or the broker is down right after.
--
-- Returns the tenant's plan as it now stands, or no row when the revision did not match (or the tenant
-- does not exist).
CREATE PROCEDURE dbo.TenantPlan_Set
    @TenantId           INT,
    @PlanCode           NVARCHAR(20),
    @PlanLimitOverrides NVARCHAR(MAX),
    @ExpectedRevision   INT,
    @PlanSource         NVARCHAR(20) = N'Operator',
    @PlanEndsAt         DATETIME2(3) = NULL,
    @OutboxMessageType  NVARCHAR(256),
    @OutboxPayload      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Updated TABLE
    (
        Id                 INT              NOT NULL,
        RefId              UNIQUEIDENTIFIER NOT NULL,
        PlanCode           NVARCHAR(20)     NOT NULL,
        PlanLimitOverrides NVARCHAR(MAX)    NULL,
        PlanRevision       INT              NOT NULL,
        PlanChangedAt      DATETIME2(3)     NULL,
        PlanSource         NVARCHAR(20)     NOT NULL,
        PlanEndsAt         DATETIME2(3)     NULL
    );

    BEGIN TRANSACTION;

    UPDATE dbo.Tenants
    SET PlanCode = @PlanCode,
        PlanLimitOverrides = @PlanLimitOverrides,
        PlanRevision = PlanRevision + 1,
        PlanChangedAt = SYSUTCDATETIME(),
        PlanSource = @PlanSource,
        PlanEndsAt = @PlanEndsAt
    OUTPUT inserted.Id, inserted.RefId, inserted.PlanCode, inserted.PlanLimitOverrides, inserted.PlanRevision, inserted.PlanChangedAt, inserted.PlanSource, inserted.PlanEndsAt
    INTO @Updated
    WHERE Id = @TenantId
      AND PlanRevision = @ExpectedRevision;

    IF @@ROWCOUNT > 0
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        VALUES (@OutboxMessageType, @OutboxPayload);
    END

    COMMIT TRANSACTION;

    SELECT
        Id,
        RefId,
        PlanCode,
        PlanLimitOverrides,
        PlanRevision,
        PlanChangedAt,
        PlanSource,
        PlanEndsAt
    FROM @Updated;
END
GO

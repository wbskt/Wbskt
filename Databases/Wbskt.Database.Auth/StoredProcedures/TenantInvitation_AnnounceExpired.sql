-- Invitations that ran out unused since the last call, up to @BatchSize, each returned once: the row
-- is marked as announced in the same statement, so a second sweep (or a second host) never returns it
-- again.
--
-- With @QueueEvents, each one's InvitationExpiredEvent goes into dbo.OutboxMessages in the same
-- transaction as the mark, dated when the invitation expired and naming every workspace of its tenant
-- (as IAuditWorkspaces.OfTenantAsync would), so a broker outage cannot lose the audit entry once the
-- row is marked. Without it (the host before this change) the caller publishes the events itself.
-- @QueueEvents defaults to 0 only for that previous host; it becomes required, or the only behaviour,
-- in the release after (#1059).
CREATE PROCEDURE dbo.TenantInvitation_AnnounceExpired
    @NowUtc DATETIME2(3),
    @BatchSize INT,
    @QueueEvents BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Due TABLE (
        RefId UNIQUEIDENTIFIER NOT NULL,
        TenantId INT NOT NULL,
        Email NVARCHAR(100) NOT NULL,
        ExpiresAt DATETIME2(3) NOT NULL);

    BEGIN TRANSACTION;

    UPDATE TOP (@BatchSize) dbo.TenantInvitations
    SET ExpiryAnnouncedAt = @NowUtc
    OUTPUT inserted.RefId, inserted.TenantId, inserted.Email, inserted.ExpiresAt
    INTO @Due (RefId, TenantId, Email, ExpiresAt)
    WHERE ExpiresAt <= @NowUtc
      AND AcceptedAt IS NULL
      AND RevokedAt IS NULL
      AND ExpiryAnnouncedAt IS NULL;

    IF @QueueEvents = 1
    BEGIN
        INSERT INTO dbo.OutboxMessages (MessageType, Payload)
        SELECT N'Wbskt.Events.Auth.InvitationExpiredEvent',
               (SELECT D.RefId AS invitationRefId,
                       D.Email AS email,
                       JSON_QUERY(CONCAT(N'[',
                           (SELECT STRING_AGG(CONVERT(NVARCHAR(12), W.Id), N',') FROM dbo.Workspaces W WHERE W.TenantId = D.TenantId),
                           N']')) AS workspaceIds,
                       CONVERT(NVARCHAR(30), D.ExpiresAt, 127) + N'Z' AS createdAtUtc
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
        FROM @Due D;
    END

    COMMIT TRANSACTION;

    SELECT RefId, TenantId, Email, ExpiresAt FROM @Due;
END
GO

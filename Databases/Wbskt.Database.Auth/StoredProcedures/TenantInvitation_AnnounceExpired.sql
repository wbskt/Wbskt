-- Invitations that ran out unused since the last call, up to @BatchSize, each returned once: the row
-- is marked as announced in the same statement, so a second sweep (or a second host) never returns it
-- again. The credential sweep calls this and logs an InvitationExpiredEvent for each row.
CREATE PROCEDURE dbo.TenantInvitation_AnnounceExpired
    @NowUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TOP (@BatchSize) dbo.TenantInvitations
    SET ExpiryAnnouncedAt = @NowUtc
    OUTPUT inserted.RefId, inserted.TenantId, inserted.Email, inserted.ExpiresAt
    WHERE ExpiresAt <= @NowUtc
      AND AcceptedAt IS NULL
      AND RevokedAt IS NULL
      AND ExpiryAnnouncedAt IS NULL;
END
GO

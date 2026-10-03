-- Retention sweep for spent credentials: refresh, password-reset and email-verification tokens, and
-- invitations nobody accepted, once they expired before @CutoffUtc. Deletes up to @BatchSize rows from
-- each table and returns the total; the caller repeats until it comes back 0.
--
-- The caller passes a cutoff well behind "now" rather than now itself: an expired token is already
-- useless, but keeping it a while longer lets a sign-in with a stolen, rotated-away token still be
-- recognised and investigated. Accepted invitations are kept as the record of how someone joined.
CREATE PROCEDURE dbo.Credential_DeleteExpired
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Deleted INT = 0;

    DELETE TOP (@BatchSize) FROM dbo.RefreshTokens WHERE Expires < @CutoffUtc;
    SET @Deleted += @@ROWCOUNT;

    DELETE TOP (@BatchSize) FROM dbo.PasswordResetTokens WHERE ExpiresAt < @CutoffUtc;
    SET @Deleted += @@ROWCOUNT;

    DELETE TOP (@BatchSize) FROM dbo.EmailVerificationTokens WHERE ExpiresAt < @CutoffUtc;
    SET @Deleted += @@ROWCOUNT;

    DELETE TOP (@BatchSize) FROM dbo.TenantInvitations WHERE ExpiresAt < @CutoffUtc AND AcceptedAt IS NULL;
    SET @Deleted += @@ROWCOUNT;

    SELECT @Deleted AS Deleted;
END
GO

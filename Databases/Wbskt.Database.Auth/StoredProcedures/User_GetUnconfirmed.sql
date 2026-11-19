-- Accounts whose owner never confirmed the address (#997): registered before @CreatedBeforeUtc, the email
-- never verified, never signed in, and no verification link still open (a resend restarts the wait).
-- Only accounts that are alone in every tenant they belong to and never paid in one: deleting them
-- touches nobody else and cancels nothing. The caller deletes each through dbo.User_Delete with
-- @UnconfirmedBefore, which checks all of this again under its locks.
--
-- Pages by Id after @AfterId, so an account the delete skips does not hold up the ones after it.
CREATE PROCEDURE dbo.User_GetUnconfirmed
    @CreatedBeforeUtc DATETIME2(3),
    @NowUtc DATETIME2(3),
    @AfterId INT,
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@BatchSize) U.Id
    FROM dbo.Users U
    WHERE U.Id > @AfterId
      AND U.IsEmailVerified = 0
      AND U.IsActive = 1
      AND U.LastLoginAt IS NULL
      AND U.CreatedAt < @CreatedBeforeUtc
      AND NOT EXISTS (
              SELECT 1
              FROM dbo.EmailVerificationTokens T
              WHERE T.UserId = U.Id
                AND T.ConsumedAt IS NULL
                AND T.ExpiresAt > @NowUtc)
      AND NOT EXISTS (
              SELECT 1
              FROM dbo.TenantMembers M
              INNER JOIN dbo.TenantMembers O ON O.TenantId = M.TenantId AND O.UserId <> M.UserId
              WHERE M.UserId = U.Id)
      AND NOT EXISTS (
              SELECT 1
              FROM dbo.TenantMembers M
              WHERE M.UserId = U.Id
                AND (EXISTS (SELECT 1 FROM dbo.TenantSubscriptions S WHERE S.TenantId = M.TenantId)
                     OR EXISTS (SELECT 1 FROM dbo.TenantCreditPurchases P WHERE P.TenantId = M.TenantId)))
    ORDER BY U.Id;
END
GO

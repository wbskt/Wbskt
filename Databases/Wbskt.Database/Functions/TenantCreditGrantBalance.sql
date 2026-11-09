-- The credits @TenantId's grants still hold at @At: granted, not drawn by a settled month, not revoked,
-- not held over an open chargeback, and not expired. Run_Start adds it to the plan's monthly allowance. NULL tenant (no plan copy yet)
-- has none.
CREATE FUNCTION dbo.TenantCreditGrantBalance (@TenantId INT, @At DATETIME2(3))
RETURNS TABLE
AS
RETURN
(
    SELECT ISNULL(SUM(g.Credits - g.Drawn), 0) AS Credits
    FROM dbo.TenantCreditGrants g
    WHERE g.TenantId = @TenantId
      AND g.RevokedAt IS NULL
      AND g.HeldAt IS NULL
      AND g.ExpiresAt > @At
);
GO

-- Gives back a grant's expiry warning claimed by dbo.TenantCreditGrants_ClaimExpiring whose event could
-- not be published, so the next run tries again.
CREATE PROCEDURE dbo.TenantCreditGrant_ReleaseExpiryWarning
    @Source    NVARCHAR(40),
    @SourceRef NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TenantCreditGrants
    SET ExpiryWarnedAt = NULL
    WHERE Source = @Source
      AND SourceRef = @SourceRef;
END
GO

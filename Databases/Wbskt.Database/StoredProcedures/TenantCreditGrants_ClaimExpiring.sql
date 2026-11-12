-- Claims the grants that expire after @From and by @To with credits still undrawn, not revoked and not held,
-- that the tenant has not been told about, marking them told at @At, and returns them with what they still
-- hold and the tenant's workspaces (comma-separated). Each grant is returned once across every replica;
-- dbo.TenantCreditGrant_ReleaseExpiryWarning gives one back when its warning could not be published.
CREATE PROCEDURE dbo.TenantCreditGrants_ClaimExpiring
    @From DATETIME2(3),
    @To   DATETIME2(3),
    @At   DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Claimed TABLE
    (
        TenantId  INT           NOT NULL,
        Source    NVARCHAR(40)  NOT NULL,
        SourceRef NVARCHAR(200) NOT NULL,
        Remaining DECIMAL(18,4) NOT NULL,
        ExpiresAt DATETIME2(3)  NOT NULL
    );

    UPDATE dbo.TenantCreditGrants
    SET ExpiryWarnedAt = @At
    OUTPUT inserted.TenantId, inserted.Source, inserted.SourceRef, inserted.Credits - inserted.Drawn, inserted.ExpiresAt
    INTO @Claimed (TenantId, Source, SourceRef, Remaining, ExpiresAt)
    WHERE ExpiresAt > @From
      AND ExpiresAt <= @To
      AND RevokedAt IS NULL
      AND HeldAt IS NULL
      AND Drawn < Credits
      AND ExpiryWarnedAt IS NULL;

    SELECT
        c.TenantId,
        c.Source,
        c.SourceRef,
        c.Remaining,
        c.ExpiresAt,
        (SELECT STRING_AGG(CAST(a.WorkspaceId AS NVARCHAR(12)), N',')
         FROM dbo.WorkspaceAccounts a
         WHERE a.TenantId = c.TenantId
           AND a.RetiredAt IS NULL) AS WorkspaceIds
    FROM @Claimed c;
END
GO

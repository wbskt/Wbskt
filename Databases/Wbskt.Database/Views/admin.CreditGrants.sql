-- Credits granted to an account outside its plan: purchases, and grants staff make.
CREATE VIEW [admin].CreditGrants
AS
SELECT
    CG.Id,
    CG.TenantId,
    CG.Credits,
    CG.Drawn,
    CG.GrantedAt,
    CG.ExpiresAt,
    CG.Source,
    CG.SourceRef,
    CG.RevokedAt,
    CG.HeldAt,
    CG.CreatedAt
FROM dbo.TenantCreditGrants CG;
GO

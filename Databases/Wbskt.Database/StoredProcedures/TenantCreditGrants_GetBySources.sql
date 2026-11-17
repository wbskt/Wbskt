-- The grants made for @Sources (JSON: [{ "Source": "dodo", "SourceRef": "pay_1" }]), with what the
-- reconcile compares against the auth database's purchases (CreditGrantReconcile): a source with no row
-- here was never granted.
CREATE PROCEDURE dbo.TenantCreditGrants_GetBySources
    @Sources NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        g.Source,
        g.SourceRef,
        CAST(CASE WHEN g.RevokedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS Revoked,
        g.HoldRevision
    FROM OPENJSON(@Sources)
    WITH
    (
        Source    NVARCHAR(40)  '$.Source',
        SourceRef NVARCHAR(200) '$.SourceRef'
    ) s
    INNER JOIN dbo.TenantCreditGrants g
        ON g.Source = s.Source
       AND g.SourceRef = s.SourceRef;
END
GO

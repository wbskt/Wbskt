-- Gives back the reconcile claim taken at @StartedAt when that run stopped partway, so the next claim
-- takes it at once. A claim another run took since is left alone.
CREATE PROCEDURE dbo.BillingReconcile_Release
    @Provider  NVARCHAR(40),
    @StartedAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BillingReconciles
    SET LastStartedAt = '2000-01-01'
    WHERE Provider = @Provider
      AND LastStartedAt = @StartedAt;
END
GO

-- Takes the provider's reconcile for this run when none started in the last @EveryMinutes, and records
-- @Now as its start. One replica gets it however many ask at once. Returns 1 when this caller got it.
CREATE PROCEDURE dbo.BillingReconcile_Claim
    @Provider     NVARCHAR(40),
    @Now          DATETIME2(3),
    @EveryMinutes INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BillingReconciles
    SET LastStartedAt = @Now
    WHERE Provider = @Provider
      AND LastStartedAt <= DATEADD(MINUTE, -@EveryMinutes, @Now);

    IF @@ROWCOUNT = 1
    BEGIN
        SELECT 1;
        RETURN;
    END

    INSERT INTO dbo.BillingReconciles (Provider, LastStartedAt)
    SELECT @Provider, @Now
    WHERE NOT EXISTS (SELECT 1 FROM dbo.BillingReconciles WITH (UPDLOCK, HOLDLOCK) WHERE Provider = @Provider);

    SELECT @@ROWCOUNT;
END
GO

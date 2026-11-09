-- Settles @TenantId's month starting @MonthStart: what its workspaces spent that month past @Allowance
-- (the plan's monthly credits) is drawn from its grants, soonest to expire first, among those that were
-- live in the month. Once per tenant and month: a second call finds the settlement and changes nothing.
-- Returns what was drawn, or NULL when the month was already settled. Run once the month's credits are
-- posted (the engine posts every few seconds; the caller waits an hour into the next month).
CREATE PROCEDURE dbo.TenantCreditGrants_Settle
    @TenantId   INT,
    @MonthStart DATE,
    @Allowance  DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @MonthEnd DATE = DATEADD(MONTH, 1, @MonthStart);

    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.TenantCreditGrantSettlements WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @TenantId AND MonthStart = @MonthStart)
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(NULL AS DECIMAL(18,4)) AS Drawn;
        RETURN;
    END

    DECLARE @Spent DECIMAL(18,4) = ISNULL((
        SELECT SUM(u.Credits)
        FROM dbo.WorkspaceCreditUsage u
        JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = u.WorkspaceId
        WHERE a.TenantId = @TenantId
          AND u.Day >= @MonthStart
          AND u.Day < @MonthEnd), 0);

    DECLARE @Overage DECIMAL(18,4) = CASE WHEN @Spent > @Allowance THEN @Spent - @Allowance ELSE 0 END;

    DECLARE @Live TABLE (Id INT NOT NULL PRIMARY KEY, Remaining DECIMAL(18,4) NOT NULL, Through DECIMAL(18,4) NOT NULL);
    INSERT INTO @Live (Id, Remaining, Through)
    SELECT
        Id,
        Credits - Drawn,
        SUM(Credits - Drawn) OVER (ORDER BY ExpiresAt, Id ROWS UNBOUNDED PRECEDING)
    FROM dbo.TenantCreditGrants WITH (UPDLOCK)
    WHERE TenantId = @TenantId
      AND ExpiresAt > @MonthStart
      AND GrantedAt < @MonthEnd
      AND (RevokedAt IS NULL OR RevokedAt > @MonthStart)
      AND Drawn < Credits;

    -- Each grant gives what is left of the overage after the grants before it, up to what it holds.
    UPDATE g
    SET Drawn = g.Drawn + CASE
            WHEN l.Through <= @Overage THEN l.Remaining
            ELSE @Overage - (l.Through - l.Remaining)
        END
    FROM dbo.TenantCreditGrants g
    JOIN @Live l ON l.Id = g.Id
    WHERE l.Through - l.Remaining < @Overage;

    DECLARE @Drawn DECIMAL(18,4) = (
        SELECT CASE WHEN ISNULL(SUM(Remaining), 0) < @Overage THEN ISNULL(SUM(Remaining), 0) ELSE @Overage END
        FROM @Live);

    INSERT INTO dbo.TenantCreditGrantSettlements (TenantId, MonthStart, Allowance, Spent, Drawn)
    VALUES (@TenantId, @MonthStart, @Allowance, @Spent, @Drawn);

    COMMIT TRANSACTION;

    SELECT @Drawn AS Drawn;
END
GO

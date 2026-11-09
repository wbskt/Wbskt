-- Draws from each tenant's credit grants the credits in @Posts that fall past its plan's monthly
-- allowance, as they are posted. Called by dbo.RunCredits_Post and dbo.Run_DeleteRetired inside their
-- transactions, after they have added the credits to dbo.WorkspaceCreditUsage.
--
-- For each tenant, the month's Posted moves by the credits just posted; the part of that step past
-- @Allowance (the allowance at this moment, so a downgrade draws nothing for what was posted before it)
-- is taken from its live grants, soonest to expire first, and added to the month's Drawn. A grant that
-- is revoked, held over a chargeback or expired at @At gives nothing. When the grants hold less than the
-- step, what they hold is drawn and the rest is past the plan: Run_Start refuses once nothing is left.
--
-- A tenant's first post of a month starts its Posted from the ledger, so a month already under way when
-- this began counts what it spent before. Lock order: the ledger rows (caller), then the tenants' month
-- rows, then their grants.
CREATE PROCEDURE dbo.TenantCreditGrants_Draw
    @Posts      dbo.TenantCreditPostTableType READONLY,
    @MonthStart DATE,
    @At         DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Steps TABLE
    (
        TenantId INT           NOT NULL PRIMARY KEY,
        Before   DECIMAL(18,4) NOT NULL,
        After    DECIMAL(18,4) NOT NULL,
        Excess     DECIMAL(18,4) NULL
    );

    BEGIN TRANSACTION;

    INSERT INTO dbo.TenantCreditMonths (TenantId, MonthStart, Posted)
    SELECT
        p.TenantId,
        @MonthStart,
        ISNULL((SELECT SUM(u.Credits)
                FROM dbo.WorkspaceCreditUsage u
                JOIN dbo.WorkspaceAccounts a ON a.WorkspaceId = u.WorkspaceId
                WHERE a.TenantId = p.TenantId
                  AND u.Day >= @MonthStart
                  AND u.Day < DATEADD(MONTH, 1, @MonthStart)), 0) - p.Credits
    FROM @Posts p
    WHERE NOT EXISTS (SELECT 1
                      FROM dbo.TenantCreditMonths m WITH (UPDLOCK, HOLDLOCK)
                      WHERE m.TenantId = p.TenantId AND m.MonthStart = @MonthStart);

    UPDATE m
    SET Posted = m.Posted + p.Credits,
        UpdatedAt = SYSUTCDATETIME()
    OUTPUT inserted.TenantId, deleted.Posted, inserted.Posted, NULL INTO @Steps (TenantId, Before, After, Excess)
    FROM dbo.TenantCreditMonths m
    JOIN @Posts p ON p.TenantId = m.TenantId
    WHERE m.MonthStart = @MonthStart;

    -- The part of each step past the allowance: all of it once the month was already past, none of it
    -- while the month stays inside.
    UPDATE s
    SET Excess = CASE WHEN s.After > p.Allowance THEN s.After - p.Allowance ELSE 0 END
             - CASE WHEN s.Before > p.Allowance THEN s.Before - p.Allowance ELSE 0 END
    FROM @Steps s
    JOIN @Posts p ON p.TenantId = s.TenantId;

    DELETE FROM @Steps WHERE Excess <= 0;

    IF EXISTS (SELECT 1 FROM @Steps)
    BEGIN
        DECLARE @Live TABLE
        (
            Id        INT           NOT NULL PRIMARY KEY,
            TenantId  INT           NOT NULL,
            Remaining DECIMAL(18,4) NOT NULL,
            Through   DECIMAL(18,4) NOT NULL
        );

        INSERT INTO @Live (Id, TenantId, Remaining, Through)
        SELECT
            g.Id,
            g.TenantId,
            g.Credits - g.Drawn,
            SUM(g.Credits - g.Drawn) OVER (PARTITION BY g.TenantId ORDER BY g.ExpiresAt, g.Id ROWS UNBOUNDED PRECEDING)
        FROM dbo.TenantCreditGrants g WITH (UPDLOCK)
        JOIN @Steps s ON s.TenantId = g.TenantId
        WHERE g.RevokedAt IS NULL
          AND g.HeldAt IS NULL
          AND g.ExpiresAt > @At
          AND g.Drawn < g.Credits;

        -- Each grant gives what is left of the step after the grants before it, up to what it holds.
        UPDATE g
        SET Drawn = g.Drawn + CASE
                WHEN l.Through <= s.Excess THEN l.Remaining
                ELSE s.Excess - (l.Through - l.Remaining)
            END
        FROM dbo.TenantCreditGrants g
        JOIN @Live l ON l.Id = g.Id
        JOIN @Steps s ON s.TenantId = l.TenantId
        WHERE l.Through - l.Remaining < s.Excess;

        UPDATE m
        SET Drawn = m.Drawn + d.Drawn
        FROM dbo.TenantCreditMonths m
        JOIN (SELECT s.TenantId,
                     CASE WHEN ISNULL(SUM(l.Remaining), 0) < s.Excess THEN ISNULL(SUM(l.Remaining), 0) ELSE s.Excess END AS Drawn
              FROM @Steps s
              LEFT JOIN @Live l ON l.TenantId = s.TenantId
              GROUP BY s.TenantId, s.Excess) d ON d.TenantId = m.TenantId
        WHERE m.MonthStart = @MonthStart
          AND d.Drawn > 0;
    END

    COMMIT TRANSACTION;
END
GO

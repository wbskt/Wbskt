-- Stamps when a run last sent through the integration. Throttled to once a minute per row, so a
-- busy workflow does not turn every send into a write.
CREATE PROCEDURE dbo.Integration_TouchLastUsed
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Integrations
    SET LastUsedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND (LastUsedAt IS NULL OR LastUsedAt < DATEADD(MINUTE, -1, SYSUTCDATETIME()));
END
GO

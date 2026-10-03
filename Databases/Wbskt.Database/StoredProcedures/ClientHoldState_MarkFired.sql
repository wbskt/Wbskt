-- Only the hold that was leased: if a non-matching message cleared it (and perhaps a new hold began)
-- since, SinceAt no longer matches and this leaves the newer state alone.
CREATE PROCEDURE dbo.ClientHoldState_MarkFired
    @Id      INT,
    @SinceAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ClientHoldStates
    SET State = N'fired', LeasedUntil = NULL, UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND State = N'holding'
      AND SinceAt = @SinceAt;
END;
GO

CREATE PROCEDURE dbo.RunCounters_AddCreditsConsumed
    @RunId INT,
    @Cost  DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RunCounters WITH (ROWLOCK)
       SET CreditsConsumed = CreditsConsumed + @Cost,
           UpdatedAt       = SYSUTCDATETIME()
    OUTPUT inserted.CreditsConsumed
     WHERE RunId = @RunId;
END;
GO

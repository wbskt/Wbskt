CREATE PROCEDURE dbo.Client_UpdateRtt
    @Id INT,
    @LastRttMs INT,
    @RttMeasuredAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET
        LastRttMs = @LastRttMs,
        RttMeasuredAt = @RttMeasuredAt
    WHERE Id = @Id;
END
GO

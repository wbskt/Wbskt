CREATE PROCEDURE dbo.JoinAggregator_Initialize
    @JoinToken    UNIQUEIDENTIFIER,
    @RunId        INT,
    @ExpectedCount INT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.JoinAggregators
    (
        JoinToken,
        RunId,
        ExpectedCount
    )
    VALUES
    (
        @JoinToken,
        @RunId,
        @ExpectedCount
    );
END;
GO

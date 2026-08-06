CREATE PROCEDURE dbo.JoinAggregator_Initialize
    @JoinToken     UNIQUEIDENTIFIER,
    @RunId         INT,
    @ExpectedCount INT,
    @Mode          NVARCHAR(20) = N'All',
    @QuorumCount   INT = 0,
    @JoinNodeId    UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.JoinAggregators
    (
        JoinToken,
        RunId,
        ExpectedCount,
        Mode,
        QuorumCount,
        JoinNodeId
    )
    VALUES
    (
        @JoinToken,
        @RunId,
        @ExpectedCount,
        @Mode,
        @QuorumCount,
        @JoinNodeId
    );
END;
GO

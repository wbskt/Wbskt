CREATE PROCEDURE dbo.JoinAggregator_Contribute
    @JoinToken   UNIQUEIDENTIFIER,
    @Outcome     NVARCHAR(20),
    @Mode        NVARCHAR(20),
    @QuorumCount INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ShouldContinue BIT = 0;

    BEGIN TRAN;

    UPDATE dbo.JoinAggregators WITH (UPDLOCK, HOLDLOCK)
    SET ContributedCount = ContributedCount + 1,
        SucceededCount   = SucceededCount + CASE WHEN @Outcome = 'succeeded' THEN 1 ELSE 0 END,
        FailedCount      = FailedCount + CASE WHEN @Outcome = 'failed' THEN 1 ELSE 0 END
    WHERE JoinToken = @JoinToken;

    DECLARE @ContributedCount INT,
            @SucceededCount   INT,
            @FailedCount      INT,
            @ExpectedCount    INT,
            @ContinueClaimed  BIT;

    SELECT
        @ContributedCount = ContributedCount,
        @SucceededCount   = SucceededCount,
        @FailedCount      = FailedCount,
        @ExpectedCount    = ExpectedCount,
        @ContinueClaimed  = ContinueClaimed
    FROM dbo.JoinAggregators
    WHERE JoinToken = @JoinToken;

    DECLARE @QuorumMet BIT = 0;

    IF @Mode = 'All' AND @ContributedCount >= @ExpectedCount
        SET @QuorumMet = 1;
    ELSE IF @Mode = 'Any' AND @ContributedCount >= 1
        SET @QuorumMet = 1;
    ELSE IF @Mode = 'Quorum' AND @SucceededCount >= @QuorumCount
        SET @QuorumMet = 1;

    IF @QuorumMet = 1 AND @ContinueClaimed = 0
    BEGIN
        UPDATE dbo.JoinAggregators
        SET ContinueClaimed = 1
        WHERE JoinToken = @JoinToken;

        SET @ShouldContinue = 1;
    END;

    COMMIT;

    SELECT
        @ShouldContinue  AS ShouldContinue,
        @ContributedCount AS ContributedCount,
        @SucceededCount   AS SucceededCount,
        @FailedCount      AS FailedCount,
        @ExpectedCount    AS ExpectedCount;
END;
GO

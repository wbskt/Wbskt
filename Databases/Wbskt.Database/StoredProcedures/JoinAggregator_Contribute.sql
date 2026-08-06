CREATE PROCEDURE dbo.JoinAggregator_Contribute
    @JoinToken UNIQUEIDENTIFIER,
    @Outcome   NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    -- Mode/QuorumCount come from the row, not from the caller: a branch that FAILS contributes from
    -- BranchLoop, which never sees the Join node's config. Before this, only the Join executor could
    -- contribute (always as 'succeeded'), so a Mode='All' cohort with a failed member never reached
    -- ExpectedCount and everything downstream of the Join was silently skipped.
    DECLARE @ShouldContinue BIT = 0;

    BEGIN TRAN;

    UPDATE dbo.JoinAggregators WITH (ROWLOCK)
    SET ContributedCount = ContributedCount + 1,
        SucceededCount   = SucceededCount + CASE WHEN @Outcome = 'succeeded' THEN 1 ELSE 0 END,
        FailedCount      = FailedCount + CASE WHEN @Outcome = 'failed' THEN 1 ELSE 0 END
    WHERE JoinToken = @JoinToken;

    DECLARE @ContributedCount INT,
            @SucceededCount   INT,
            @FailedCount      INT,
            @ExpectedCount    INT,
            @Mode             NVARCHAR(20),
            @QuorumCount      INT,
            @JoinNodeId       UNIQUEIDENTIFIER,
            @ContinueClaimed  BIT;

    SELECT
        @ContributedCount = ContributedCount,
        @SucceededCount   = SucceededCount,
        @FailedCount      = FailedCount,
        @ExpectedCount    = ExpectedCount,
        @Mode             = Mode,
        @QuorumCount      = QuorumCount,
        @JoinNodeId       = JoinNodeId,
        @ContinueClaimed  = ContinueClaimed
    FROM dbo.JoinAggregators
    WHERE JoinToken = @JoinToken;

    DECLARE @QuorumMet BIT = 0;

    -- 'All' counts arrivals regardless of outcome, so a cohort completes even when members failed.
    IF @Mode = 'All' AND @ContributedCount >= @ExpectedCount
        SET @QuorumMet = 1;
    ELSE IF @Mode = 'Any' AND @ContributedCount >= 1
        SET @QuorumMet = 1;
    ELSE IF @Mode = 'Quorum' AND @SucceededCount >= @QuorumCount
        SET @QuorumMet = 1;
    -- Quorum can also become unreachable: once too many have failed, no further success is possible,
    -- so release the continuation rather than leaving the run parked forever.
    ELSE IF @Mode = 'Quorum' AND (@ExpectedCount - @FailedCount) < @QuorumCount
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
        @ShouldContinue   AS ShouldContinue,
        @ContributedCount AS ContributedCount,
        @SucceededCount   AS SucceededCount,
        @FailedCount      AS FailedCount,
        @ExpectedCount    AS ExpectedCount,
        @JoinNodeId       AS JoinNodeId;
END;
GO

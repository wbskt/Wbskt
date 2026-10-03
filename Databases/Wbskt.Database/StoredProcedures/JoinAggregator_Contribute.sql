CREATE PROCEDURE dbo.JoinAggregator_Contribute
    @JoinToken UNIQUEIDENTIFIER,
    @BranchId  BIGINT = NULL, -- NULL only from an engine older than this procedure, mid-deploy
    @Outcome   NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Mode/QuorumCount come from the row, not from the caller: a branch that FAILS contributes from
    -- BranchLoop, which never sees the Join node's config. Before this, only the Join executor could
    -- contribute (always as 'succeeded'), so a Mode='All' cohort with a failed member never reached
    -- ExpectedCount and everything downstream of the Join was silently skipped.
    DECLARE @ShouldContinue BIT = 0;

    BEGIN TRAN;

    -- A replayed contribution from the same branch counts once. The aggregator row lock serialises
    -- contributors to a cohort, so the existence check cannot race.
    DECLARE @CohortLock BIGINT, @IsRepeat BIT = 0;
    SELECT @CohortLock = Id FROM dbo.JoinAggregators WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE JoinToken = @JoinToken;
    IF EXISTS (SELECT 1 FROM dbo.JoinContributions WHERE JoinToken = @JoinToken AND BranchId = @BranchId)
        SET @IsRepeat = 1;

    IF @IsRepeat = 0
    BEGIN
        IF @BranchId IS NOT NULL
            INSERT INTO dbo.JoinContributions (JoinToken, BranchId) VALUES (@JoinToken, @BranchId);

        UPDATE dbo.JoinAggregators WITH (ROWLOCK)
        SET ContributedCount = ContributedCount + 1,
            SucceededCount   = SucceededCount + CASE WHEN @Outcome = 'succeeded' THEN 1 ELSE 0 END,
            FailedCount      = FailedCount + CASE WHEN @Outcome = 'failed' THEN 1 ELSE 0 END
        WHERE JoinToken = @JoinToken;
    END;

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

    IF @QuorumMet = 1 AND @ContinueClaimed = 0 AND @IsRepeat = 0
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

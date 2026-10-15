CREATE PROCEDURE dbo.JoinAggregator_Contribute
    @JoinToken UNIQUEIDENTIFIER,
    @BranchId  BIGINT = NULL, -- NULL only from an engine older than this procedure, mid-deploy
    @Outcome   NVARCHAR(20) -- 'succeeded', 'failed', or 'declined' (ended without reaching the Join)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Mode/QuorumCount come from the row, not from the caller: a branch that FAILS contributes from
    -- BranchLoop, which never sees the Join node's config. Before this, only the Join executor could
    -- contribute (always as 'succeeded'), so a Mode='All' cohort with a failed member never reached
    -- ExpectedCount and everything downstream of the Join was silently skipped.
    DECLARE @ShouldContinue BIT = 0;

    -- A 'declined' vote comes from a member that ended (an End node, a port with no edge) without
    -- reaching the Join. It used to be silent: a Mode='All' cohort never reached ExpectedCount, the
    -- other members ended as "not the last", the run finalized Succeeded and everything after the Join
    -- was skipped without a trace. A decline is not an arrival: it takes the member out of the cohort
    -- (ExpectedCount is the members still expected to arrive), never claims the continuation itself,
    -- and abandons the join once the join can no longer be met.
    DECLARE @Declined BIT = CASE WHEN @Outcome = 'declined' THEN 1 ELSE 0 END;
    DECLARE @Abandoned BIT = 0;

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

        IF @Declined = 1
            UPDATE dbo.JoinAggregators WITH (ROWLOCK)
            SET ExpectedCount = ExpectedCount - 1
            WHERE JoinToken = @JoinToken;
        ELSE
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

    IF @Declined = 1
    BEGIN
        -- 'All' needs every member, so one walking away fails the join. 'Any' and 'Quorum' carry on
        -- without it until the remaining members can no longer meet them. A cohort with no Join (a
        -- Fork saved before every Fork had to have one) has nothing to abandon.
        IF @IsRepeat = 0 AND @ContinueClaimed = 0 AND @JoinNodeId IS NOT NULL
           AND (@Mode = 'All'
                OR (@Mode = 'Any' AND @ContributedCount = 0 AND @ExpectedCount <= 0)
                OR (@Mode = 'Quorum' AND (@ExpectedCount - @FailedCount) < @QuorumCount))
            SET @Abandoned = 1;
    END
    -- 'All' counts arrivals regardless of outcome, so a cohort completes even when members failed.
    ELSE IF @Mode = 'All' AND @ContributedCount >= @ExpectedCount
        SET @QuorumMet = 1;
    ELSE IF @Mode = 'Any' AND @ContributedCount >= 1
        SET @QuorumMet = 1;
    ELSE IF @Mode = 'Quorum' AND @SucceededCount >= @QuorumCount
        SET @QuorumMet = 1;
    -- Quorum can also become unreachable: once too many have failed, no further success is possible,
    -- so release the continuation rather than leaving the run parked forever.
    ELSE IF @Mode = 'Quorum' AND (@ExpectedCount - @FailedCount) < @QuorumCount
        SET @QuorumMet = 1;

    -- An abandoned join is claimed so no later arrival can continue past it.
    IF @Abandoned = 1
        UPDATE dbo.JoinAggregators
        SET ContinueClaimed = 1
        WHERE JoinToken = @JoinToken;

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
        @JoinNodeId       AS JoinNodeId,
        @Abandoned        AS Abandoned;
END;
GO

CREATE PROCEDURE dbo.Bookmark_ClaimDue
    @Now       DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Claim-by-delete is atomic and needs no lease columns/host id: once a row is deleted here it
    -- cannot be claimed again. The wake it decides is stored on the branch in the same transaction:
    -- WokenAt for every claim, and for a TTL on a wait that is not a timer the port the branch leaves
    -- by. A stop between this claim and the dispatch then loses only the in-memory dispatch, and the
    -- next leader's RunRecoveryService resumes the branch (Branch_GetWoken) exactly as the dispatch
    -- would have: the branch loop takes PendingTakePort, and a timer's node finds its deadline passed.
    DECLARE @Claimed TABLE
    (
        Id                BIGINT           NOT NULL,
        RefId             UNIQUEIDENTIFIER NOT NULL,
        RunId             INT              NOT NULL,
        BranchRefId       UNIQUEIDENTIFIER NOT NULL,
        NodeId            UNIQUEIDENTIFIER NOT NULL,
        WakeConditionKind NVARCHAR(64)     NOT NULL,
        MatchKey          NVARCHAR(400)    NOT NULL,
        WakeConditionJson NVARCHAR(MAX)    NOT NULL,
        ExpiresAt         DATETIME2(3)     NULL,
        TtlPort           NVARCHAR(128)    NULL,
        CreatedAt         DATETIME2(3)     NOT NULL
    );

    BEGIN TRANSACTION;

    DELETE TOP (@BatchSize) FROM dbo.Bookmarks WITH (ROWLOCK, READPAST)
    OUTPUT deleted.Id, deleted.RefId, deleted.RunId, deleted.BranchRefId, deleted.NodeId,
           deleted.WakeConditionKind, deleted.MatchKey, deleted.WakeConditionJson,
           deleted.ExpiresAt, deleted.TtlPort, deleted.CreatedAt
    INTO @Claimed
    WHERE ExpiresAt <= @Now;

    UPDATE br
    SET
        WokenAt = SYSUTCDATETIME(),
        PendingTakePort = CASE
            WHEN c.TtlPort IS NOT NULL AND c.TtlPort <> N'' AND c.WakeConditionKind <> N'timer' THEN c.TtlPort
            ELSE br.PendingTakePort
        END
    FROM dbo.Branches br WITH (ROWLOCK)
    INNER JOIN @Claimed c ON c.BranchRefId = br.RefId
    WHERE br.Status = N'Waiting';

    COMMIT TRANSACTION;

    SELECT
        Id,
        RefId,
        RunId,
        BranchRefId,
        NodeId,
        WakeConditionKind,
        MatchKey,
        WakeConditionJson,
        ExpiresAt,
        TtlPort,
        CreatedAt
    FROM @Claimed;
END;
GO

CREATE PROCEDURE dbo.Bookmark_Create
    @RefId             UNIQUEIDENTIFIER,
    @RunId             INT,
    @BranchRefId       UNIQUEIDENTIFIER,
    @NodeId            UNIQUEIDENTIFIER,
    @WakeConditionKind NVARCHAR(64),
    @MatchKey          NVARCHAR(400),
    @WakeConditionJson NVARCHAR(MAX),
    @ExpiresAt         DATETIME2(3),
    @TtlPort           NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Bookmarks
        (RefId, RunId, BranchRefId, NodeId, WakeConditionKind, MatchKey, WakeConditionJson, ExpiresAt, TtlPort)
    VALUES
        (@RefId, @RunId, @BranchRefId, @NodeId, @WakeConditionKind, @MatchKey, @WakeConditionJson, @ExpiresAt, @TtlPort);

    DECLARE @NewId INT = SCOPE_IDENTITY();

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
    FROM dbo.Bookmarks
    WHERE Id = @NewId;
END;
GO

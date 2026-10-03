CREATE PROCEDURE dbo.ClientPresenceCheck_Insert
    @TriggerKey  NVARCHAR(400),
    @ClientRefId UNIQUEIDENTIFIER,
    @State       NVARCHAR(16),
    @ChangedAt   DATETIME2(3),
    @DueAt       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    -- Idempotent: the same change arriving twice (a redelivered event) leaves one row.
    INSERT INTO dbo.ClientPresenceChecks (TriggerKey, ClientRefId, State, ChangedAt, DueAt)
    SELECT @TriggerKey, @ClientRefId, @State, @ChangedAt, @DueAt
    WHERE NOT EXISTS (SELECT 1
                      FROM dbo.ClientPresenceChecks WITH (UPDLOCK, HOLDLOCK)
                      WHERE TriggerKey = @TriggerKey
                        AND ChangedAt = @ChangedAt);
END;
GO

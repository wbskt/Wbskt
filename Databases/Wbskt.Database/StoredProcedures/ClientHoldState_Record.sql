-- Records one client message against one hold trigger. @Matches is whether the trigger's filter
-- passed on it. A matching message starts a hold (or refreshes the payload of one in progress); a
-- message that does not match clears it, which also re-arms a trigger that already fired.
CREATE PROCEDURE dbo.ClientHoldState_Record
    @TriggerKey  NVARCHAR(400),
    @Matches     BIT,
    @EventAt     DATETIME2(3),
    @HoldSeconds INT,
    @Payload     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @State NVARCHAR(16), @LastEventAt DATETIME2(3);

    SELECT @State = State, @LastEventAt = LastEventAt
    FROM dbo.ClientHoldStates WITH (UPDLOCK, HOLDLOCK)
    WHERE TriggerKey = @TriggerKey;

    IF @State IS NULL
    BEGIN
        INSERT INTO dbo.ClientHoldStates (TriggerKey, State, LastEventAt, SinceAt, DueAt, Payload)
        VALUES (
            @TriggerKey,
            CASE WHEN @Matches = 1 THEN N'holding' ELSE N'clear' END,
            @EventAt,
            CASE WHEN @Matches = 1 THEN @EventAt END,
            CASE WHEN @Matches = 1 THEN DATEADD(SECOND, @HoldSeconds, @EventAt) END,
            CASE WHEN @Matches = 1 THEN @Payload END);
    END
    -- Older than what this trigger has already seen: it arrived out of order and says nothing new.
    ELSE IF @EventAt >= @LastEventAt
    BEGIN
        IF @Matches = 0
            UPDATE dbo.ClientHoldStates
            SET State = N'clear', LastEventAt = @EventAt, SinceAt = NULL, DueAt = NULL, Payload = NULL,
                LeasedUntil = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE TriggerKey = @TriggerKey;
        ELSE IF @State = N'clear'
            UPDATE dbo.ClientHoldStates
            SET State = N'holding', LastEventAt = @EventAt, SinceAt = @EventAt,
                DueAt = DATEADD(SECOND, @HoldSeconds, @EventAt), Payload = @Payload,
                LeasedUntil = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE TriggerKey = @TriggerKey;
        ELSE IF @State = N'holding'
            UPDATE dbo.ClientHoldStates
            SET LastEventAt = @EventAt, Payload = @Payload, UpdatedAt = SYSUTCDATETIME()
            WHERE TriggerKey = @TriggerKey;
        ELSE -- fired: still matching, so it stays quiet until a message stops matching
            UPDATE dbo.ClientHoldStates
            SET LastEventAt = @EventAt, UpdatedAt = SYSUTCDATETIME()
            WHERE TriggerKey = @TriggerKey;
    END;

    COMMIT TRANSACTION;
END;
GO

-- Whether a refusal at @Kind's limit through policy @PolicyId is to be announced: 1 when none was in the
-- last @IntervalSeconds before @NowUtc, recording this one, else 0. Two replicas refusing at once announce
-- it once between them.
CREATE PROCEDURE dbo.RegistrationLimitNotice_Claim
    @PolicyId        INT,
    @Kind            NVARCHAR(16),
    @NowUtc          DATETIME2(3),
    @IntervalSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @Last DATETIME2(3);
    DECLARE @Known BIT = 0;
    SELECT @Last = LastNotifiedAt, @Known = 1
    FROM dbo.RegistrationLimitNotices WITH (UPDLOCK, HOLDLOCK)
    WHERE PolicyId = @PolicyId
      AND Kind = @Kind;

    DECLARE @Claimed BIT = 0;
    IF @Known = 0
    BEGIN
        INSERT INTO dbo.RegistrationLimitNotices (PolicyId, Kind, LastNotifiedAt)
        SELECT @PolicyId, @Kind, @NowUtc
        WHERE EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE Id = @PolicyId);
        SET @Claimed = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;
    END
    ELSE IF @Last <= DATEADD(SECOND, -@IntervalSeconds, @NowUtc)
    BEGIN
        UPDATE dbo.RegistrationLimitNotices
        SET LastNotifiedAt = @NowUtc
        WHERE PolicyId = @PolicyId
          AND Kind = @Kind;
        SET @Claimed = 1;
    END

    COMMIT TRANSACTION;

    SELECT @Claimed AS Claimed;
END
GO
